using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalAI.Core;
public sealed class ProjectMemory
{
    public DateTimeOffset? DeletedAt {get;set;}
    public bool DeleteChats {get;set;}
    public Guid Id {get;set;}=Guid.NewGuid();
    public string Name {get;set;}="New project";
    public string IsolationRepository {get;set;}="";
    public string IsolationCommit {get;set;}="";
    public List<string> Roots {get;set;}=[];
    public string Purpose {get;set;}="";
    public string Conventions {get;set;}="";
    public string BuildCommand {get;set;}="";
    public string TestCommand {get;set;}="";
    public string UnfinishedWork {get;set;}="";
    public override string ToString()=>Name;
    public void Validate()
    {
        if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||Name.Length>120||Roots==null||Roots.Count>16||new[]{Purpose,Conventions,BuildCommand,TestCommand,UnfinishedWork}.Any(s=>s==null||s.Length>16000))throw new IOException("Enter a project name, up to 16 optional folders and notes/commands under 16,000 characters.");
        var workspace=new FileWorkspace(Roots);foreach(var root in Roots)workspace.Resolve(root);
    }
}
public sealed record TaskEvidence(Guid Id,string Kind,string Script,string Directory,string Status,int? ExitCode,string Output,string LogPath,string SourceHash,DateTimeOffset Recorded);
public sealed class AgentNotice
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public DateTimeOffset Created {get;set;}=DateTimeOffset.UtcNow;
    public string Kind {get;set;}="";
    public string Text {get;set;}="";
    public Guid? TaskId {get;set;}
    public Guid? ApprovalId {get;set;}
    public bool Read {get;set;}
    public override string ToString()=>$"{(Read?"":"● ")}{Kind}: {Text[..Math.Min(Text.Length,100)]}";
}
public sealed class TrackedChange
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid ApprovalId {get;set;}
    public int Index {get;set;}
    public FileActionPlan Plan {get;set;}=null!;
    public FileActionReceipt? Receipt {get;set;}
    public string AppliedHash {get;set;}="";
    public string Status {get;set;}="Awaiting review";
    public override string ToString()=>$"{Plan.Action.Operation}: {Plan.Destination??Plan.Source} • {Status}";
}
public sealed record WorkspaceSearchHit(string Kind,string Location,string Excerpt,Guid? ChatId=null);
public static class AgentWorkspaceFeatures
{
    public static ProjectMemory ProposedNotes(ProjectMemory current,string json)
    {
        if(json.Length>50000)throw new IOException("Project notes proposal is too large.");using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=4});if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new IOException("Project notes must be an object.");var fields=new Dictionary<string,string>();
        foreach(var field in doc.RootElement.EnumerateObject())if(field.Name is not ("purpose" or "conventions" or "unfinishedWork")||field.Value.ValueKind!=JsonValueKind.String||!fields.TryAdd(field.Name,field.Value.GetString()!))throw new IOException("Only unique purpose, conventions and unfinishedWork text fields are allowed. Configure folders and commands manually.");
        var result=new ProjectMemory{Id=current.Id,Name=current.Name,Roots=[..current.Roots],Purpose=fields.GetValueOrDefault("purpose",current.Purpose),Conventions=fields.GetValueOrDefault("conventions",current.Conventions),UnfinishedWork=fields.GetValueOrDefault("unfinishedWork",current.UnfinishedWork),BuildCommand=current.BuildCommand,TestCommand=current.TestCommand,DeletedAt=current.DeletedAt,DeleteChats=current.DeleteChats,IsolationRepository=current.IsolationRepository,IsolationCommit=current.IsolationCommit};result.Validate();return result;
    }
    public static string Snapshot(ProjectMemory project,CancellationToken token=default)
    {
        project.Validate();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in project.Roots.Order(StringComparer.OrdinalIgnoreCase))foreach(var path in ContextFiles.Discover(root,token).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();var file=new FileWorkspace(project.Roots).Read(path,token);if(!seen.Add(file.Path))continue;
            total+=Encoding.UTF8.GetByteCount(file.Content);if(total>64*1024*1024)throw new IOException("Verification snapshot exceeds 64 MiB; connect a smaller project.");
            var bytes=Encoding.UTF8.GetBytes(file.Path+"\0"+file.Content+"\0");hash.AppendData(bytes);
        }
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{project.Id,project.Roots,project.BuildCommand,project.TestCommand})));return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string Verification(AgentTask task,ProjectMemory? project,string currentHash,bool pending)
    {
        if(project==null||task.ProjectId!=project.Id)return "Choose the task's project before verifying completion.";
        if(task.FollowUps.Any(f=>f.Status=="Pending"))return "Apply or resolve saved follow-ups before verifying completion.";
        if(pending)return "Review or decline pending actions before verifying completion.";
        foreach(var kind in new[]{"build","test"})
        {
            var expected=kind=="build"?project.BuildCommand:project.TestCommand;
            if(string.IsNullOrWhiteSpace(expected))return $"Configure the project's {kind} command first.";
            var evidence=task.Evidence.LastOrDefault(e=>e.Kind==kind);
            if(evidence==null||task.VerificationSince.HasValue&&evidence.Recorded<task.VerificationSince.Value||evidence.Script!=expected||evidence.SourceHash!=currentHash||evidence.ExitCode!=0||evidence.Status!="Succeeded")return $"A successful reviewed {kind} run against the current project sources is required.";
            try{new FileWorkspace(project.Roots).Resolve(evidence.Directory);}catch(IOException){return $"The {kind} command ran outside the current project folders; review and rerun it in this project.";}
        }
        return "Verified";
    }
    public static async Task<IReadOnlyList<WorkspaceSearchHit>> Search(string query,IEnumerable<Conversation> chats,IEnumerable<string> roots,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(query)||query.Length>200)throw new IOException("Enter a search of 1–200 characters.");var results=new List<WorkspaceSearchHit>();
        string Excerpt(string text,int index)=>text.Substring(Math.Max(0,index-80),Math.Min(320,text.Length-Math.Max(0,index-80))).Replace('\n',' ');
        foreach(var chat in chats)
        {
            token.ThrowIfCancellationRequested();foreach(var text in new[]{chat.Title,chat.TaskBrief}.Concat(chat.Messages.Select(m=>m.Content)))
            {var at=text.IndexOf(query,StringComparison.OrdinalIgnoreCase);if(at>=0){results.Add(new("Chat",chat.Title,Excerpt(text,at),chat.Id));break;}}if(results.Count>=100)return results;
        }
        return await Task.Run(()=>
        {
            var workspace=new FileWorkspace(roots);var scanned=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var root in workspace.Roots)foreach(var path in ContextFiles.Discover(root,token))
            {
                token.ThrowIfCancellationRequested();if(!seen.Add(path))continue;if(++scanned>500)throw new IOException("Search exceeds 500 files; narrow connected folders.");
                try{var file=workspace.Read(path,token);var at=file.Content.IndexOf(query,StringComparison.OrdinalIgnoreCase);if(at>=0||Path.GetFileName(path).Contains(query,StringComparison.OrdinalIgnoreCase))results.Add(new("File",path,at<0?"File name match":Excerpt(file.Content,at)));}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ }
                if(results.Count>=100)return (IReadOnlyList<WorkspaceSearchHit>)results;
            }
            return results;
        },token);
    }
    public static string FileHash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static FileAction Rollback(TrackedChange change,FileWorkspace workspace)
    {
        if(change.Status!="Applied"||change.Receipt==null)throw new IOException("Only a recorded applied change can be rolled back.");
        var plan=change.Plan;var target=plan.Destination??plan.Source;
        if(plan.Action.Operation is "write" or "copy" or "move" or "restore")
        {workspace.Resolve(target);if(!File.Exists(target)||FileHash(target)!=change.AppliedHash)throw new IOException("The changed file has been edited since apply. Review it manually; rollback refused.");}
        return plan.Action.Operation switch
        {
            "write"=>plan.SourceHash==null?new("delete",plan.Source):new("write",plan.Source,Content:plan.Before),
            "copy" or "restore"=>new("delete",target),
            "move"=>new("move",target,plan.Source),
            "delete"=>new("restore",change.Receipt.RecoveryPath??throw new IOException("Recovery copy missing."),plan.Source),
            "mkdir"=>new("rmdir",plan.Source),
            _=>throw new IOException("Read-only actions have no changes to roll back.")
        };
    }
    public static string Diff(FileActionPlan plan)
    {
        var before=plan.Before.Replace("\r\n","\n").Split('\n');var after=plan.After.Replace("\r\n","\n").Split('\n');var prefix=0;while(prefix<Math.Min(before.Length,after.Length)&&before[prefix]==after[prefix])prefix++;var suffix=0;while(suffix<Math.Min(before.Length,after.Length)-prefix&&before[^(suffix+1)]==after[^(suffix+1)])suffix++;
        return $"--- {plan.Source}\n+++ {plan.Destination??plan.Source}\n@@ after line {prefix} @@\n"+string.Join('\n',before.Skip(Math.Max(0,prefix-2)).Take(Math.Min(2,prefix)).Select(s=>" "+s).Concat(before.Skip(prefix).Take(before.Length-prefix-suffix).Select(s=>"-"+s)).Concat(after.Skip(prefix).Take(after.Length-prefix-suffix).Select(s=>"+"+s)).Concat(after.Skip(after.Length-suffix).Take(2).Select(s=>" "+s)));
    }
}
