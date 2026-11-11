using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LocalAI.Core;
public sealed record WorkspacePlan(string Repository,string Commit,string Destination,string Git,string Hooks);
public static class IsolatedWorkspace
{
    private static async Task<string> Git(string tool,string folder,IEnumerable<string> arguments,CancellationToken token)
    {
        var info=new ProcessStartInfo(tool){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var key in info.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())info.Environment.Remove(key);
        info.Environment["GIT_TERMINAL_PROMPT"]="0";
        foreach(var arg in arguments)info.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=info};if(!process.Start())throw new IOException("Git did not start.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
        async Task<string> Read(StreamReader reader){var result=new System.Text.StringBuilder();var buffer=new char[2048];int n;while((n=await reader.ReadAsync(buffer,timeout.Token))>0){if(result.Length+n>1024*1024)throw new IOException("Git output exceeds 1 MiB.");result.Append(buffer,0,n);}return result.ToString();}
        try{var output=Read(process.StandardOutput);var errors=Read(process.StandardError);await Task.WhenAll(output,errors,process.WaitForExitAsync(timeout.Token));if(process.ExitCode!=0)throw new IOException("Git could not complete the operation: "+(await errors)[..Math.Min(2000,(await errors).Length)]);return (await output).Trim();}
        catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}throw;}
    }
    public static async Task<WorkspacePlan> Preview(string repository,string revision,string managedDirectory,CancellationToken token=default)
    {
        repository=Path.GetFullPath(repository);new FileWorkspace([repository]).Resolve(repository);
        if(!Directory.Exists(Path.Combine(repository,".git"))&&!File.Exists(Path.Combine(repository,".git")))throw new IOException("Choose a Git working-copy root.");
        if(string.IsNullOrWhiteSpace(revision)||revision.Length>200||revision.StartsWith('-')||revision.Any(char.IsControl))throw new IOException("Enter a branch or commit, such as HEAD.");
        var tool=SourceControl.FindTool(SourceProvider.Git)??throw new IOException("Install Git for Windows first.");
        if(tool.StartsWith(repository.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Git must be installed outside the project.");
        var commit=await Git(tool,repository,["-c","core.fsmonitor=false","rev-parse","--verify","--end-of-options",revision+"^{commit}"],token);
        if(!Regex.IsMatch(commit,"^[a-fA-F0-9]{40,64}$"))throw new IOException("Git did not resolve one exact commit.");
        var root=Path.GetFullPath(managedDirectory);Directory.CreateDirectory(root);new FileWorkspace([root]).Resolve(root);
        var hooks=Path.Combine(root,"disabled-hooks");Directory.CreateDirectory(hooks);if(Directory.EnumerateFileSystemEntries(hooks).Any())throw new IOException("Managed disabled-hooks directory must be empty.");new FileWorkspace([root]).Resolve(hooks);
        return new(repository,commit,Path.Combine(root,Guid.NewGuid().ToString("N")),tool,hooks);
    }
    public static async Task Create(WorkspacePlan plan,CancellationToken token=default)
    {
        if(Directory.Exists(plan.Destination)||File.Exists(plan.Destination))throw new IOException("Isolated workspace destination already exists.");
        new FileWorkspace([Path.GetDirectoryName(plan.Destination)!]).Resolve(plan.Destination);
        if(Directory.EnumerateFileSystemEntries(plan.Hooks).Any())throw new IOException("Disabled-hooks directory changed; preview again.");
        var args=new List<string>{"-c","core.fsmonitor=false","-c","core.hooksPath="+plan.Hooks,"-c","core.symlinks=false","-c","submodule.recurse=false"};
        // Filters execute programs during checkout. Keep the repository's raw committed blobs instead.
        var filters=await Git(plan.Git,plan.Repository,["-c","core.fsmonitor=false","config","--name-only","--list"],token);
        foreach(var key in filters.Split('\n').Where(k=>k.StartsWith("filter.",StringComparison.OrdinalIgnoreCase)).Distinct())
        {
            var end=key.LastIndexOf('.');if(end<=7)continue;var prefix=key[..end];foreach(var value in new[]{".process=",".smudge=",".required=false"}){args.Add("-c");args.Add(prefix+value);}
        }
        args.AddRange(["worktree","add","--detach","--",plan.Destination,plan.Commit]);
        try
        {
            await Git(plan.Git,plan.Repository,args,token);
            var actual=await Git(plan.Git,plan.Destination,["-c","core.fsmonitor=false","rev-parse","HEAD"],token);
            if(actual!=plan.Commit||!File.Exists(Path.Combine(plan.Destination,".git")))throw new IOException("Created workspace did not match the reviewed commit.");
        }
        catch(Exception e){throw new IOException("Workspace creation did not finish verification. Any partial folder and Git registration were retained for inspection: "+plan.Destination+". "+e.Message,e);}
    }
}
