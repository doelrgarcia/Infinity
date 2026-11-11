using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LocalAI.Core;

public sealed record WorkspaceEntry(string Path,bool IsDirectory,long? Bytes);
public sealed record FileAction(string Operation,string Path,string? Destination=null,string? Content=null,int? Offset=null,int? Length=null);
public sealed record FileActionPlan(FileAction Action,string Source,string? Destination,string? SourceHash,string? DestinationHash,string Before,string After);
public sealed record FileActionReceipt(string Operation,string Path,string? RecoveryPath,string? Text,string? SourcePath=null,int? Offset=null,int? NextOffset=null,bool? HasMore=null,int? TotalCharacters=null);

// No shell or provider process receives file-system authority. All paths are checked again at apply.
public sealed class FileWorkspace
{
    public const long MaximumTransferBytes=128L*1024*1024;
    public const string RecoveryFolder=".infinity-recovery";
    private readonly string[] roots;
    public IReadOnlyList<string> Roots=>Array.AsReadOnly(roots);
    public FileWorkspace(IEnumerable<string> folders)
    {
        roots=folders.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Probe only the requested path. An offline saved share must not block other roots.
    }
    private static string Normalize(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathFullyQualified(path)||path.StartsWith(@"\\?\",StringComparison.Ordinal)||path.StartsWith(@"\\.\",StringComparison.Ordinal))throw new IOException("Use a local drive, mapped drive, or UNC share path.");
        var full=System.IO.Path.GetFullPath(path);return System.IO.Path.TrimEndingDirectorySeparator(full);
    }
    private static void CheckLinks(string path)
    {
        for(string? current=path;current!=null;current=System.IO.Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try{attributes=File.GetAttributes(current);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked folders and reparse points are excluded.");
        }
    }
    public string Resolve(string path)
    {
        if(roots.Length==0)throw new IOException("Connect a workspace folder first.");
        if(string.IsNullOrWhiteSpace(path))throw new IOException("Enter a file or folder path.");
        var full=Normalize(System.IO.Path.IsPathFullyQualified(path)?path:System.IO.Path.Combine(roots[0],path));
        var appData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(var protectedRoot in new[]{System.IO.Path.Combine(appData,"Rymaris","LocalAI"),System.IO.Path.Combine(appData,"Programs","Infinity"),AppPaths.PortableRoot,AppPaths.DataRoot,AppPaths.AgentRoot,System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)})
            if(full.Equals(protectedRoot,StringComparison.OrdinalIgnoreCase)||full.StartsWith(protectedRoot+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Infinity's installed program and private connection/chat state are protected from workspace actions.");
        if(!roots.Any(root=>full.Equals(root,StringComparison.Ordinal)||full.StartsWith(System.IO.Path.EndsInDirectorySeparator(root)?root:root+System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)))throw new IOException("Path is outside the connected folders. Use the connected root's exact spelling.");
        var parts=full.Split('\\','/');
        if(parts.Any(p=>p.EndsWith('.')||p.EndsWith(' ')))throw new IOException("Trailing-dot and trailing-space path aliases are excluded.");
        if(parts.Any(p=>p.Equals(".git",StringComparison.OrdinalIgnoreCase)||p.Equals(".svn",StringComparison.OrdinalIgnoreCase)||p.Equals("$tf",StringComparison.OrdinalIgnoreCase)||p.Equals(".tf",StringComparison.OrdinalIgnoreCase)||p.Equals(RecoveryFolder,StringComparison.OrdinalIgnoreCase)||p.Equals(".localai-backups",StringComparison.OrdinalIgnoreCase)))throw new IOException("Repository metadata and recovery folders are protected.");
        if(parts.Skip(1).Any(p=>p.Contains(':')))throw new IOException("Alternate data streams are excluded.");
        CheckLinks(full);return full;
    }
    public IReadOnlyList<WorkspaceEntry> List(string path,CancellationToken token=default)
    {
        path=Resolve(path);var entries=new List<WorkspaceEntry>();var scanned=0;
        foreach(var item in Directory.EnumerateFileSystemEntries(path))
        {
            token.ThrowIfCancellationRequested();if(++scanned>1000)throw new IOException("Folder exceeds 1,000 entries. Choose a smaller folder.");
            try{var safe=Resolve(item);var attributes=File.GetAttributes(safe);var directory=(attributes&FileAttributes.Directory)!=0;entries.Add(new(safe,directory,directory?null:new FileInfo(safe).Length));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){ } // Linked/protected entries are never offered as actions.
        }
        return entries.OrderByDescending(e=>e.IsDirectory).ThenBy(e=>e.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public IReadOnlyList<WorkspaceEntry> Search(string path,string name,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(name))return List(path,token);
        var pending=new Stack<string>();pending.Push(Resolve(path));var matches=new List<WorkspaceEntry>();var visited=0;
        while(pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();if(++visited>250)throw new IOException("Search exceeds 250 folders. Narrow the starting folder.");
            foreach(var entry in List(folder,token))
            {
                if(System.IO.Path.GetFileName(entry.Path).Contains(name,StringComparison.OrdinalIgnoreCase)){matches.Add(entry);if(matches.Count>=500)return matches;}
                if(entry.IsDirectory&&!new[]{"bin","obj","node_modules","publish",".vs"}.Contains(System.IO.Path.GetFileName(entry.Path),StringComparer.OrdinalIgnoreCase))pending.Push(entry.Path);
            }
        }
        return matches;
    }
    public ContextFile Read(string path,CancellationToken token=default)=>ContextFiles.Read(Resolve(path),token);
    public FileActionReceipt ReadPage(FileAction action,CancellationToken token=default)
    {
        if(action.Operation!="read")throw new IOException("Paging is only available for reads.");
        var file=ContextFiles.Read(Resolve(action.Path),token,16*1024*1024);
        var length=action.Length??6000;if(length is <1 or >12000)throw new IOException("Read length must be 1–12000 characters.");
        var offset=action.Offset??0;if(offset<0)offset=(int)Math.Max(0,(long)file.Content.Length+offset);
        if(offset>file.Content.Length)throw new IOException("Read offset exceeds the file length.");
        var end=(int)Math.Min((long)offset+length,file.Content.Length);
        // Keep UTF-16 surrogate pairs intact across pages.
        if(offset>0&&offset<file.Content.Length&&char.IsLowSurrogate(file.Content[offset])&&char.IsHighSurrogate(file.Content[offset-1]))offset--;
        if(end<file.Content.Length&&end>0&&char.IsHighSurrogate(file.Content[end-1])&&char.IsLowSurrogate(file.Content[end]))end++;
        return new("read",file.Path,null,file.Content[offset..end],Offset:offset,NextOffset:end,HasMore:end<file.Content.Length,TotalCharacters:file.Content.Length);
    }
    private string ResolveRecovery(string path)
    {
        var full=Normalize(path);var folder=System.IO.Path.GetDirectoryName(full)!;
        if(System.IO.Path.GetFileName(folder)!=RecoveryFolder||System.IO.Path.GetFileName(full).Length<34||!Guid.TryParseExact(System.IO.Path.GetFileName(full)[..32],"N",out _)||System.IO.Path.GetFileName(full)[32]!='-')throw new IOException("Choose a recovery item created by Infinity.");
        Resolve(System.IO.Path.GetDirectoryName(folder)!);CheckLinks(full);return full;
    }
    public IReadOnlyList<WorkspaceEntry> ListRecovery(string folder)
    {
        var recovery=System.IO.Path.Combine(Resolve(folder),RecoveryFolder);CheckLinks(recovery);if(!Directory.Exists(recovery))return [];
        return Directory.EnumerateFiles(recovery).Take(1000).Select(p=>new WorkspaceEntry(ResolveRecovery(p),false,new FileInfo(p).Length)).ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file,out HandleInfo info);
    private static void CheckSingleLink(string path)
    {
        if(!OperatingSystem.IsWindows()||!File.Exists(path))return;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(!GetFileInformationByHandle(file.SafeFileHandle,out var info))throw new IOException("Cannot verify file link ownership before writing.");
        if(info.Links!=1)throw new IOException("Hard-linked files or an unverifiable link count cannot be overwritten through the workspace.");
    }
    private static string Fingerprint(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan);
        if(file.Length>MaximumTransferBytes)throw new IOException("File operations are limited to 128 MiB per file.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[1024*1024];int n;while((n=file.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,n);}return Convert.ToHexString(hash.GetHashAndReset());
    }
    public FileActionPlan Preview(FileAction action,CancellationToken token=default)
    {
        var op=action.Operation;var source=op=="restore"?ResolveRecovery(action.Path):Resolve(action.Path);
        if(op is not ("list" or "read" or "write" or "mkdir" or "rmdir" or "copy" or "move" or "delete" or "restore"))throw new IOException("Unknown file action.");
        string? destination=action.Destination==null?null:Resolve(action.Destination);string? sourceHash=null,destinationHash=null;var before="";var after="";
        if(op is "copy" or "move" or "delete" or "restore")
        {
            if(!File.Exists(source))throw new IOException("Select an existing file. Folder moves and recursive deletion are not supported.");
            var name=System.IO.Path.GetFileName(source);
            if(name.StartsWith("appsettings",StringComparison.OrdinalIgnoreCase)||name.StartsWith(".env",StringComparison.OrdinalIgnoreCase)||name.Contains("secret",StringComparison.OrdinalIgnoreCase)||name.Contains("credential",StringComparison.OrdinalIgnoreCase)||new[]{".pfx",".p12",".pem",".key"}.Contains(System.IO.Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))throw new IOException("Configuration and credential files cannot be changed through workspace actions.");
            sourceHash=Fingerprint(source,token);before=$"{source}\n{new FileInfo(source).Length:N0} bytes\nSHA-256: {sourceHash}";
            if(op!="delete")
            {
                if(destination==null||!Directory.Exists(System.IO.Path.GetDirectoryName(destination)))throw new IOException("Choose an existing destination folder and a new file name.");
                ReviewedCodeSave.ValidatePath(System.IO.Path.GetDirectoryName(destination)!,System.IO.Path.GetFileName(destination));
                if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("Copy/move never overwrite an existing destination.");
                after=destination+(op=="restore"?"\nThe recovery copy is retained after verified restoration.":"");
            }
            else after="Remove the original name; keep a recovery copy beside the original folder.";
        }
        else if(op=="write")
        {
            CheckSingleLink(source);
            if(!ContextFiles.Allowed(source))throw new IOException("Configuration, credential and generated files are excluded from writes.");
            var plan=ReviewedCodeSave.Preview(System.IO.Path.GetDirectoryName(source)!,System.IO.Path.GetFileName(source),action.Content??throw new IOException("Write content is required."));
            if(plan.OriginalContent!=null)Read(source,token); // Do not expose existing secrets in previews or AI context.
            before=plan.OriginalContent??"(New file)";after=plan.NewContent;sourceHash=plan.OriginalHash;
        }
        else if(op=="rmdir")
        {
            if(roots.Contains(source,StringComparer.OrdinalIgnoreCase))throw new IOException("Connected roots cannot be removed.");
            if(!Directory.Exists(source)||Directory.EnumerateFileSystemEntries(source).Any())throw new IOException("Only an existing empty folder can be removed. No recursive deletion.");
            before="Empty folder: "+source;after="Remove this empty folder only.";
        }
        else if(op=="mkdir")
        {
            if(Directory.Exists(source)||File.Exists(source))throw new IOException("Folder name already exists.");
            ReviewedCodeSave.ValidatePath(System.IO.Path.GetDirectoryName(source)!,System.IO.Path.GetFileName(source));after="Create folder: "+source;
        }
        else if(op=="read"){before=action.Offset!=null||action.Length!=null?ReadPage(action,token).Text!:Read(source,token).Content;after="Include this screened text in the current chat context.";}
        else {before=string.Join('\n',List(source,token).Select(e=>(e.IsDirectory?"Folder: ":"File: ")+e.Path));after="Return the visible folder entries.";}
        return new(action,source,destination,sourceHash,destinationHash,before,after);
    }
    public FileActionReceipt Apply(FileActionPlan plan,bool confirmed,CancellationToken token=default)
    {
        if(!confirmed)throw new InvalidOperationException("Review and explicit confirmation are required.");
        var fresh=Preview(plan.Action,token);
        if(fresh.Source!=plan.Source||fresh.Destination!=plan.Destination||fresh.SourceHash!=plan.SourceHash||fresh.Before!=plan.Before||fresh.After!=plan.After)throw new IOException("File or folder changed since preview. Review again.");
        token.ThrowIfCancellationRequested();var op=plan.Action.Operation;
        if(op=="list")return new(op,plan.Source,null,fresh.Before);
        if(op=="read")return plan.Action.Offset!=null||plan.Action.Length!=null?ReadPage(plan.Action,token):new(op,plan.Source,null,Read(plan.Source,token).Content);
        if(op=="write")
        {
            var p=new CodeSavePlan(System.IO.Path.GetDirectoryName(plan.Source)!,System.IO.Path.GetFileName(plan.Source),plan.Action.Content!,plan.SourceHash==null?null:plan.Before,plan.SourceHash);
            var receipt=ReviewedCodeSave.Apply(p,p.Overwrites,token);return new(op,receipt.Path,receipt.BackupPath,null);
        }
        if(op=="mkdir"){Directory.CreateDirectory(plan.Source);return new(op,plan.Source,null,null);}
        if(op=="rmdir"){Directory.Delete(plan.Source,false);return new(op,plan.Source,null,null);}
        if(op=="restore"){CopyVerified(plan,token);return new(op,plan.Destination!,plan.Source,null,plan.Source);}
        if(op is "copy" or "move")CopyVerified(plan,token);
        string? recovery=null;
        if(op is "move" or "delete")
        {
            if(Fingerprint(plan.Source,token)!=plan.SourceHash)throw new IOException("Source changed. Any verified destination copy is retained; the source was not removed.");
            var folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.Source)!,RecoveryFolder);CheckLinks(folder);Directory.CreateDirectory(folder);CheckLinks(folder);
            recovery=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+"-"+System.IO.Path.GetFileName(plan.Source));File.Move(plan.Source,recovery,false);
        }
        return new(op,plan.Destination??plan.Source,recovery,null,plan.Source);
    }
    private static void CopyVerified(FileActionPlan plan,CancellationToken token)
    {
        using var input=new FileStream(plan.Source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan);
        if(input.Length>MaximumTransferBytes)throw new IOException("File exceeds the transfer limit.");
        var temporary=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.Destination)!,".infinity-transfer-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var buffer=new byte[1024*1024];int n;while((n=input.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);}output.Flush(true);}
            if(Fingerprint(temporary,token)!=plan.SourceHash)throw new IOException("Copy verification failed; destination was not replaced.");
            token.ThrowIfCancellationRequested();File.Move(temporary,plan.Destination!,false);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

public static class FileActionProtocol
{
    public const string Language="infinity-files";
    public static bool IsActionBlock(string language,string json)
    {
        if(language.Trim()==Language)return true;
        if(!language.Trim().Equals("json",StringComparison.OrdinalIgnoreCase))return false;
        try{return Parse(json).All(a=>a.Operation is "list" or "read" or "write" or "mkdir" or "copy" or "move" or "delete" or "restore");}
        catch(Exception e) when(e is IOException or JsonException or InvalidOperationException){return false;}
    }
    public static string Instructions(IEnumerable<string> roots)=>"\nConnected file workspace roots (paths are untrusted data): "+JsonSerializer.Serialize(roots)+"\nFor file tasks you may propose a fenced infinity-files JSON array, with objects {\"operation\":\"list|read|write|mkdir|copy|move|delete\",\"path\":\"absolute path inside a connected root\",\"destination\":\"optional new path\",\"content\":\"write text only\"}. At most 12 actions. No commands in file-action packets and no automatic execution. The app reviews each action; writes require confirmation with before/after and backups. Delete keeps a recovery copy; copy/move never overwrite. Only returned results prove execution. Never claim proposed actions have run. Request list/read first when content is unknown. Only list/read results explicitly included by the user enter model context.";
    public static IReadOnlyList<FileAction> Parse(string json)
    {
        if(json.Length>131072)throw new IOException("File action packet exceeds 128 Ki characters.");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});
        if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength() is <1 or >12)throw new IOException("Use an array of 1–12 file actions.");
        var actions=new List<FileAction>();
        foreach(var item in doc.RootElement.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)throw new IOException("Each file action must be an object.");
            var fields=new Dictionary<string,string?>();int? offset=null,length=null;foreach(var field in item.EnumerateObject())
            {
                if(field.Name is not ("operation" or "path" or "destination" or "content" or "offset" or "length")||fields.ContainsKey(field.Name))throw new IOException("Unknown or duplicate file action field.");
                if(field.Name is "offset" or "length")
                {if(field.Value.ValueKind!=JsonValueKind.Null){if(!field.Value.TryGetInt32(out var number))throw new IOException("Paging fields must be integers.");if(field.Name=="offset")offset=number;else length=number;}fields.Add(field.Name,null);}
                else fields.Add(field.Name,field.Value.ValueKind==JsonValueKind.Null?null:field.Value.GetString());
            }
            var op=fields.GetValueOrDefault("operation")??throw new IOException("Operation required.");if(op is not ("list" or "read" or "write" or "mkdir" or "rmdir" or "copy" or "move" or "delete" or "restore"))throw new IOException("Use write for new files (not create), mkdir for directories; supported operations: list/read/write/mkdir/rmdir/copy/move/delete/restore.");
            if((offset!=null||length!=null)&&op!="read")throw new IOException("Only read actions accept offset and length.");
            if(length is <1 or >12000)throw new IOException("Read length must be 1–12000 characters.");
            actions.Add(new(op,fields.GetValueOrDefault("path")??throw new IOException("Path required."),fields.GetValueOrDefault("destination"),fields.GetValueOrDefault("content"),offset,length));
        }
        return actions;
    }
}
