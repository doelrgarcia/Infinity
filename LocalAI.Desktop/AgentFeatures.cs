using System.IO;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private AgentWorkbenchView? agentWorkbench;
    private System.Windows.Controls.Button? agentEntry;
    private void EnsureAgentState(){if(agentState!=null)return;agentStore=new(AgentDirectory);agentState=agentStore.Load();}
    private ProjectMemory? ProjectForChat()
    {if(conversation.ProjectId==null)return null;var state=agentState??new AgentStore(AgentDirectory).Load();var project=state.Projects.SingleOrDefault(p=>p.Id==conversation.ProjectId)??throw new IOException("This chat's project memory is unavailable. Open Workspace & inbox to restore or choose its project.");return project.DeletedAt==null?project:null;}
    internal void AttachProjectToCurrentChat(Guid id)
    {
        if(ChatManagementBlocked)throw new IOException("Finish the current chat operation and review before attaching a project.");EnsureAgentState();var project=VisibleProjects.Single(p=>p.Id==id);store.EnsureCurrent(conversation);conversation.ProjectId=id;conversation.WorkspaceRoots=[..project.Roots];store.Save(conversation);UpdateContext();status.Text="Project notes and folder scope attached: "+project.Name+". Existing history and included sources are retained.";
    }
    private string TrackReviewedCodeSave(CodeSavePlan plan,CodeSaveReceipt receipt)
    {
        try
        {
            if(!ContextFiles.Allowed(receipt.Path)||ContextFiles.ContainsSensitiveContent(plan.NewContent)||plan.OriginalContent!=null&&ContextFiles.ContainsSensitiveContent(plan.OriginalContent))return "Saved code; sensitive/configuration content excluded from Changes. ";
            var roots=new[]{Path.GetDirectoryName(receipt.Path)!};var workspace=new FileWorkspace(roots);workspace.Read(receipt.Path);EnsureAgentState();if(Agent.Changes.Count>=2000)throw new IOException("Change history is full.");
            var approval=new AgentApproval{Kind="files",Value=JsonSerializer.Serialize(new[]{new{operation="write",path=receipt.Path,content=plan.NewContent}}),Status="Reviewed save applied",ProjectId=conversation.ProjectId,Roots=[..roots]};Agent.Approvals.Add(approval);
            var filePlan=new FileActionPlan(new("write",receipt.Path,Content:plan.NewContent),receipt.Path,null,plan.OriginalHash,null,plan.OriginalContent??"(New file)",plan.NewContent);
            Agent.Changes.Add(new(){ApprovalId=approval.Id,Plan=filePlan,Receipt=new("write",receipt.Path,receipt.BackupPath,null),Status="Applied",AppliedHash=receipt.Hash});Notice("Saved code",Path.GetFileName(receipt.Path)+" was reviewed and saved. It is available in Changes.",approval:approval.Id);SaveAgent();return "";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){return "Code saved, but Changes history could not be recorded: "+e.Message+". The save receipt retains any recovery path. ";}
    }
    private ProjectMemory? AgentProject(AgentTask? task=null){var project=Agent.Projects.SingleOrDefault(p=>p.Id==(task==null?Agent.ActiveProjectId:task.ProjectId));if(project?.DeletedAt!=null)throw new IOException("Restore the deleted project before continuing its tasks.");return project;}
    private IReadOnlyList<string> AgentRoots(AgentTask? task=null)=>AgentProject(task)?.Roots??(task?.ProjectId!=null?throw new IOException("This task's project is unavailable. Restore its project before continuing."):(IReadOnlyList<string>)Agent.Roots);
    private void Notice(string kind,string text,Guid? task=null,Guid? approval=null)
    {
        if(Agent.Inbox.Count>=2000){var removable=Agent.Inbox.FirstOrDefault(n=>n.Read);if(removable!=null)Agent.Inbox.Remove(removable);else{Agent.InboxOverflowCount=Math.Min(int.MaxValue-1,Agent.InboxOverflowCount)+1;return;}}
        Agent.Inbox.Add(new(){Kind=kind,Text=text,TaskId=task,ApprovalId=approval});
    }
    internal void OpenAgentWorkbench(string view="Projects")
    {
        if(agentState==null)OpenAgent();if(agentState==null)return;
        agentWorkbench??=new(this,Agent);ShowAgentSurface(agentWorkbench);agentWorkbench.ShowView(view);
    }
    internal void SaveProject(ProjectMemory project,bool select=true)
    {
        if(agentRequest!=null)throw new IOException("Wait for the Agent reply before changing its project settings.");
        project.Validate();var original=Agent.Projects.SingleOrDefault(p=>p.Id==project.Id);if(original?.DeletedAt!=null)throw new IOException("Restore this project from Deleted items before editing it.");if(original!=null){project.IsolationRepository=original.IsolationRepository;project.IsolationCommit=original.IsolationCommit;}var at=Agent.Projects.FindIndex(p=>p.Id==project.Id);var previous=at<0?null:Agent.Projects[at];var active=Agent.ActiveProjectId;
        if(at<0){if(VisibleProjects.Count()>=100||Agent.Projects.Count>=1000)throw new IOException("Project limit reached.");Agent.Projects.Add(project);}else Agent.Projects[at]=project;
        try{if(select)Agent.ActiveProjectId=project.Id;SaveAgent();}
        catch{if(at<0)Agent.Projects.Remove(project);else Agent.Projects[at]=previous!;Agent.ActiveProjectId=active;throw;}
        RefreshProjectNavigation();
    }
    internal void SelectAgentProject(Guid? id){if(agentRequest!=null)throw new IOException("Wait for the current Agent reply before switching projects.");if(id.HasValue&&!VisibleProjects.Any(p=>p.Id==id))throw new IOException("Project not found.");Agent.ActiveProjectId=id;SaveAgent();}
    internal IReadOnlyList<string> AgentConnectedRoots=>AgentRoots();
    internal void SetAgentFolders(List<string> roots){var workspace=new FileWorkspace(roots);foreach(var root in roots)workspace.Resolve(root);if(AgentProject() is { } project){SaveProject(new(){Id=project.Id,Name=project.Name,Roots=roots,Purpose=project.Purpose,Conventions=project.Conventions,BuildCommand=project.BuildCommand,TestCommand=project.TestCommand,UnfinishedWork=project.UnfinishedWork});}else{Agent.Roots=roots;SaveAgent();}}
    private async Task<IReadOnlyList<WorkspaceSearchHit>> SearchAgentScope(string query,IEnumerable<string> roots,CancellationToken token)
    {
        var chats=VisibleChats.ToList();chats.Add(new(){Id=Guid.Empty,Title="Infinity Agent",TaskBrief=Agent.Memory,Messages=Agent.Messages.ToList()});
        var hits=await AgentWorkspaceFeatures.Search(query,chats,roots,token);return hits.Select(h=>h.ChatId==Guid.Empty?h with{Kind="Agent",ChatId=null}:h).ToArray();
    }
    internal Task<IReadOnlyList<WorkspaceSearchHit>> SearchAgentWorkspace(string query,CancellationToken token)=>SearchAgentScope(query,AgentRoots(),token);
    internal async Task OpenSearchResult(WorkspaceSearchHit hit)
    {
        if(hit.Kind=="Agent"){OpenAgent();AgentView.ReviewText(this,"Agent search result",hit.Excerpt,false);return;}
        if(hit.ChatId.HasValue){await ChatAssistantAction("open",hit.ChatId.Value,"");return;}
        var file=await Task.Run(()=>new FileWorkspace(AgentRoots()).Read(hit.Location));AgentView.ReviewText(this,"Search result: "+file.Path,file.Content,false);
    }
    internal async Task<string> VerifyAgentTask(AgentTask task,CancellationToken token=default)
    {
        var project=AgentProject(task);var hash=project==null?"":await Task.Run(()=>AgentWorkspaceFeatures.Snapshot(project,token),token);
        var result=AgentWorkspaceFeatures.Verification(task,project,hash,Agent.Approvals.Any(a=>a.TaskId==task.Id&&a.Status is "Pending review" or "Reviewing"));
        if(result=="Verified"){task.VerifiedAt=DateTimeOffset.UtcNow;task.Status="Verified complete";Notice("Verified completion",task.Title+" — reviewed build and test passed for source snapshot "+hash,task.Id);}else{task.VerifiedAt=null;if(task.Status=="Verified complete")task.Status="Needs verification";}
        SaveAgent();return result;
    }
    internal async Task QueueRollback(TrackedChange change)
    {
        var approval=Agent.Approvals.Single(a=>a.Id==change.ApprovalId);var roots=approval.Roots.Count>0?approval.Roots:AgentRoots();var action=await Task.Run(()=>AgentWorkspaceFeatures.Rollback(change,new FileWorkspace(roots)));
        var id=QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation=action.Operation,path=action.Path,destination=action.Destination,content=action.Content}}),Agent.Tasks.SingleOrDefault(t=>t.Id==approval.TaskId),approval.ProjectId,roots);
        Agent.Approvals.Last().RollbackOf=change.Id;SaveAgent();_ = id;
    }
    private void AttachChangeTracking(FileActionReviewWindow review,AgentApproval approval,int index)
    {
        TrackedChange? tracked=null;
        review.BeforeApply=plan=>tracked=BeginTrackedChange(approval,index,plan);
        review.Closed+=(_,_)=>{if(tracked!=null&&review.Receipt!=null)CompleteTrackedChange(tracked,approval,review.Receipt);};
    }
    private TrackedChange BeginTrackedChange(AgentApproval approval,int index,FileActionPlan plan)
    {
            if(approval.RollbackOf is { } prior){var original=Agent.Changes.Single(c=>c.Id==prior);AgentWorkspaceFeatures.Rollback(original,new FileWorkspace(approval.Roots.Count>0?approval.Roots:AgentRoots()));}
            if(Agent.Changes.Count>=2000)throw new IOException("Change history is full. Export history before applying more changes.");
            var tracked=new TrackedChange{ApprovalId=approval.Id,Index=index,Plan=plan,Status="Applying — outcome must be checked after interruption"};Agent.Changes.Add(tracked);SaveAgent();return tracked;
    }
    private void CompleteTrackedChange(TrackedChange tracked,AgentApproval approval,FileActionReceipt receipt)
    {
            tracked.Receipt=receipt;tracked.Status="Applied";
            var target=tracked.Plan.Destination??tracked.Plan.Source;if(File.Exists(target)&&tracked.Plan.Action.Operation is "write" or "copy" or "move" or "restore")tracked.AppliedHash=AgentWorkspaceFeatures.FileHash(target);
            if(approval.RollbackOf is { } prior&&Agent.Changes.SingleOrDefault(c=>c.Id==prior) is { } original)original.Status="Rolled back";
            if(Agent.Tasks.SingleOrDefault(t=>t.Id==approval.TaskId) is { } task){task.VerifiedAt=null;}
            SaveAgent();
    }
    private async Task RecordCommandEvidence(AgentApproval approval,CommandRun run,string before)
    {
        if(approval.Purpose is not ("build" or "test")||Agent.Tasks.SingleOrDefault(t=>t.Id==approval.TaskId) is not { } task)return;
        var project=Agent.Projects.SingleOrDefault(p=>p.Id==approval.ProjectId);if(project==null)throw new IOException("The command ran, but its project is unavailable; completion evidence was not recorded.");
        var after=await Task.Run(()=>AgentWorkspaceFeatures.Snapshot(project));var hash=before==after?after:"Sources changed during the run — rerun against stable sources";
        task.Evidence.Add(new(run.Id,approval.Purpose,run.Request.Script,run.Request.WorkingDirectory,run.Status,run.ExitCode,run.Output,run.LogPath,hash,DateTimeOffset.UtcNow));task.VerifiedAt=null;
    }
    private async Task<string> AgentFeatureTool(AgentCall call,string modelId,AgentTask? task,CancellationToken token)
    {
        switch(call.Action)
        {
            case "searchWorkspace":return JsonSerializer.Serialize(await SearchAgentScope(call.Value,AgentRoots(task),token));
            case "inbox":return JsonSerializer.Serialize(Agent.Inbox.Where(n=>!n.Read).TakeLast(30));
            case "completeTask":
                var target=task??(Guid.TryParse(call.Target,out var id)?Agent.Tasks.SingleOrDefault(t=>t.Id==id):null)??throw new IOException("Use an exact task ID.");return await VerifyAgentTask(target,token);
            case "projectMemory":
                var project=AgentProject(task)??throw new IOException("Choose a project in Workspace first.");return QueueApproval("project",call.Value,task,project.Id,project.Roots);
            case "browserElements":return await (agentBrowser??throw new IOException("Open a browser page first.")).AgentElements();
            case "browserFill":case "browserClick":case "browserSubmit":
                var browser=agentBrowser??throw new IOException("Open a browser page first.");var action=call.Action=="browserFill"?"fill":call.Action=="browserSubmit"?"submit":"click";
                var plan=await browser.PreviewAgentAction(action,call.Target,call.Value);return QueueApproval("browser",JsonSerializer.Serialize(plan),task);
            case "requestStrongerModel":
                var notice="Local assistance requested a stronger model: "+call.Value;Notice("Model help",notice,task?.Id);SaveAgent();return "A routing offer was added to Inbox. The user can choose a provider; nothing was sent to a cloud model.";
            default:throw new IOException("Unsupported tool.");
        }
    }
    private ModelProfile SelectLocalFirst(string selected)
    {
        var choice=settings.Models.Single(p=>p.Id==selected);if(!Agent.LocalFirst||!choice.IsCloud)return choice;
        return settings.Models.Where(p=>!p.IsCloud&&File.Exists(p.Executable)&&(File.Exists(p.ModelPath)||Directory.Exists(p.ModelPath))&&p.ContextLimit>=8192).OrderBy(p=>p.ContextLimit).FirstOrDefault()??throw new IOException("No suitable local model is installed. Install one or explicitly turn off Local first in Routing.");
    }
    private async Task<string> AgentGenerateWithRouting(string id,IReadOnlyList<Message> packet,CancellationToken token)
    {
        var profile=SelectLocalFirst(id);
        try{return await AgentGenerate(profile.Id,packet,token);}
        catch(Exception e) when((e is not OperationCanceledException||!token.IsCancellationRequested)&&!profile.IsCloud&&Agent.OfferCloudHelp)
        {
            token.ThrowIfCancellationRequested();Notice("Model help","Local model could not finish: "+e.Message+". A reviewed stronger-model handoff is available.");SaveAgent();
            var owner=this;var dialog=new CliHandoffWindow(settings.Models,Agent.PreferredCloudId,packet,"Local Agent needs help: "+e.Message){Owner=owner};
            if(agentRoutingTest!=null)dialog.Loaded+=(_,_)=>agentRoutingTest(dialog);
            ModelProfile provider;
            agentReviewWait="Review needed · local model needs help · choose Keep local or review the CLI handoff";SaveAgent();
            try{if(dialog.ShowDialog()!=true||dialog.SelectedProvider is not { } selected)throw new IOException("Stronger-model handoff declined. Local progress and pending reviews are retained.");provider=selected;}
            finally{agentReviewWait=null;SaveAgent();}
            // The existing handoff is advisory and may excerpt context. Wrap it back into a
            // protocol request, then use the normal exact cloud review before transmission.
            var limit=CliAssistant.MaximumPromptCharacters-AgentProtocol.Instructions.Length-240;var context=dialog.Packet.Prompt;var messages=new List<Message>{new("system",AgentProtocol.Instructions),new("user","Help with the latest Agent request using this bounded context (excerpts may be omitted):\n"+(context.Length<=limit?context:throw new IOException("Required Agent handoff exceeds the CLI limit. Shorten the scope; no instructions were truncated.")),Images:ImageInput.All(dialog.Packet.Messages))};
            return await AgentGenerate(provider.Id,messages,token);
        }
    }
    private Action<CliHandoffWindow>? agentRoutingTest=null;
}
