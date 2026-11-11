using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using System.Xml;
using System.Xml.Linq;

namespace LocalAI.Core;

public enum SourceProvider { Git, Subversion, TFVC }
public sealed record SourceRequest(SourceProvider Provider,string Folder,string Operation,string? File=null,string? Message=null);
public sealed record SourceResult(int ExitCode,string Output);
public sealed record SourcePlan(SourceRequest Request,string Executable,string Fingerprint,string Review);

// A fixed operation vocabulary, never a shell or an arbitrary command field.
public sealed class SourceControl
{
    private readonly FileWorkspace workspace;
    private readonly Func<SourceProvider,string?> tools;
    public SourceControl(IEnumerable<string> roots,Func<SourceProvider,string?>? tools=null)
    {workspace=new(roots);this.tools=tools??FindTool;}
    public static string? FindTool(SourceProvider provider)
    {
        var name=provider switch {SourceProvider.Git=>"git.exe",SourceProvider.Subversion=>"svn.exe",SourceProvider.TFVC=>"tf.exe",_=>throw new ArgumentException("Unknown provider")};
        var candidates=new List<string>();
        var program=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.AddRange(provider switch {
            SourceProvider.Git=>new[]{Path.Combine(program,"Git","cmd",name)},
            SourceProvider.Subversion=>new[]{Path.Combine(program,"SlikSvn","bin",name),Path.Combine(program,"TortoiseSVN","bin",name),Path.Combine(program,"VisualSVN","bin",name),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","InfinityTools","Subversion","1.15.0","bin",name)},
            _=>Array.Empty<string>()});
        if(provider==SourceProvider.TFVC)
            foreach(var basePath in new[]{program,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
                foreach(var year in new[]{"2022","2019","18"})foreach(var edition in new[]{"Community","Professional","Enterprise","BuildTools"})
                    candidates.Add(Path.Combine(basePath,"Microsoft Visual Studio",year,edition,"Common7","IDE","CommonExtensions","Microsoft","TeamFoundation","Team Explorer",name));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Where(Path.IsPathFullyQualified).Select(p=>Path.Combine(p,name)));
        return candidates.FirstOrDefault(File.Exists)??(provider==SourceProvider.Git?FindManagedGit(StartupDependencies.DefaultRoot):null);
    }
    // Resolve an already verified managed install without changing PATH or downloading.
    public static string? FindManagedGit(string dependencyRoot)
    {
        try
        {
            var package=DependencyCatalog.Load().Packages.Single(p=>p.Id=="git-runtime");
            using var installer=new DependencyInstaller(dependencyRoot);
            return installer.IsInstalled(package)?DependencyInstaller.SafePath(installer.PackagePath(package),"cmd/git.exe"):null;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){return null;}
    }
    public static string SetupUrl(SourceProvider provider)=>provider switch {
        SourceProvider.Git=>"https://git-scm.com/download/win",
        SourceProvider.Subversion=>"https://subversion.apache.org/packages.html#windows",
        SourceProvider.TFVC=>"https://learn.microsoft.com/en-us/azure/devops/repos/tfvc/use-team-foundation-version-control-commands",
        _=>throw new ArgumentException("Unknown provider")};
    public static bool IsReadOnly(string operation)=>operation is "status" or "diff" or "history" or "branches";
    private string Folder(SourceRequest request)
    {
        var folder=workspace.Resolve(request.Folder);
        if(!Directory.Exists(folder))throw new IOException("Choose an existing connected working folder.");
        // Never discover a repository above the selected scope.
        if(request.Provider==SourceProvider.Git&&!Directory.Exists(Path.Combine(folder,".git"))&&!File.Exists(Path.Combine(folder,".git")))throw new IOException("Select the Git working-copy root (the folder containing .git).");
        if(request.Provider==SourceProvider.Subversion&&!Directory.Exists(Path.Combine(folder,".svn")))throw new IOException("Select the Subversion working-copy root (the folder containing .svn).");
        return folder;
    }
    private string Selected(SourceRequest request)
    {
        var folder=Folder(request);
        if(string.IsNullOrWhiteSpace(request.File))throw new IOException("Select one file for this operation. Repository-wide commits are excluded.");
        var file=workspace.Resolve(Path.IsPathFullyQualified(request.File)?request.File:Path.Combine(folder,request.File));
        var prefix=Path.EndsInDirectorySeparator(folder)?folder:folder+Path.DirectorySeparatorChar;
        if(!file.StartsWith(prefix,StringComparison.Ordinal)||Directory.Exists(file))throw new IOException("Select a file inside this working copy.");
        var name=Path.GetFileName(file);
        if(name.StartsWith("appsettings",StringComparison.OrdinalIgnoreCase)||name.StartsWith(".env",StringComparison.OrdinalIgnoreCase)||name.Contains("secret",StringComparison.OrdinalIgnoreCase)||name.Contains("credential",StringComparison.OrdinalIgnoreCase)||new[]{".pem",".pfx",".key",".p12"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))throw new IOException("Private configuration and credential files are excluded.");
        return Path.GetRelativePath(folder,file).Replace('\\','/');
    }
    private static string Message(SourceRequest request)
    {
        var text=request.Message?.Trim();if(string.IsNullOrEmpty(text)||text.Length>2000||text.Contains('\0'))throw new IOException("Enter a commit message (1–2,000 characters).");return text;
    }
    public IReadOnlyList<string> Arguments(SourceRequest request)
    {
        Folder(request);var op=request.Operation;
        string[] result=request.Provider switch {
            SourceProvider.Git=>op switch {
                "status"=>["status","--short","--branch","--untracked-files=normal"],
                "diff"=>["diff","--no-ext-diff","--no-textconv","HEAD","--"],
                "history"=>["log","-20","--format=%h %s"],
                "branches"=>["branch","--list"],
                "stage"=>["add","--",Selected(request)],
                "unstage"=>["restore","--staged","--",Selected(request)],
                "commit"=>["commit","--no-gpg-sign","--only","-m",Message(request),"--",Selected(request)],
                "fetch"=>["fetch","--no-recurse-submodules","origin"],
                "push"=>["push","--no-verify","--recurse-submodules=no","origin","HEAD"],
                _=>throw new IOException("Unsupported Git operation.")},
            SourceProvider.Subversion=>op switch {
                "status"=>["status","--ignore-externals","."],
                "diff"=>["diff","--internal-diff","--ignore-properties","."],
                "history"=>["log","--limit","20","-r","HEAD:1","."],
                "stage"=>["add","--depth","empty","--",Selected(request)+"@"],
                "commit"=>["commit","--depth","empty","-m",Message(request),"--",Selected(request)+"@"],
                _=>throw new IOException("Unsupported Subversion operation.")},
            SourceProvider.TFVC=>op switch {
                "status"=>["status",".","/recursive","/format:detailed","/noprompt"],
                "diff"=>["difference","./"+Selected(request),"/format:unified","/noprompt"],
                "history"=>["history",".","/recursive","/stopafter:20","/format:brief","/noprompt"],
                "stage"=>["add","./"+Selected(request),"/noprompt"],
                "checkout"=>["checkout","./"+Selected(request),"/noprompt"],
                "commit"=>["checkin","./"+Selected(request),"/comment:"+Message(request),"/noprompt"],
                _=>throw new IOException("Unsupported TFVC operation.")},
            _=>throw new IOException("Unsupported provider.")};
        return request.Provider==SourceProvider.TFVC?new[]{"vc"}.Concat(result).ToArray():result;
    }
    private string Tool(SourceProvider provider)
    {
        var path=tools(provider);if(path==null||!Path.IsPathFullyQualified(path)||!File.Exists(path)||!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Provider CLI not found. Install the official client or enter its full .exe path.");
        path=Path.GetFullPath(path);
        for(string? part=path;part!=null;part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked CLI paths are excluded. Select the installed executable directly.");
        // A repository-provided tool must never be discovered and executed implicitly.
        foreach(var root in workspace.Roots)if(path.StartsWith(root.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Select a trusted tool installed outside connected workspace folders.");
        return Path.GetFullPath(path);
    }
    public async Task<SourceResult> Read(SourceRequest request,CancellationToken token=default)
    {if(!IsReadOnly(request.Operation))throw new IOException("This operation requires a reviewed plan.");return await Run(request,Tool(request.Provider),token);}
    private async Task<string> State(SourceRequest request,string tool,CancellationToken token)
    {
        var status=await Run(request with{Operation="status"},tool,token);if(status.ExitCode!=0)throw new IOException(status.Output);
        // A new TFVC add has no server BASE yet. Review its exact local contents instead.
        var diff=request.Provider==SourceProvider.TFVC&&request.Operation=="stage"?new SourceResult(0,"New TFVC add: no BASE diff exists. Review selected local content below."):await Run(request with{Operation="diff"},tool,token);
        // A new Git repository has no HEAD; use the working-tree diff instead.
        if(diff.ExitCode!=0&&request.Provider==SourceProvider.Git)diff=await Run(request with{Operation="diff"},tool,token,true);
        if(diff.ExitCode!=0)throw new IOException(diff.Output);
        string identity="";
        if(request.Provider==SourceProvider.Git)
        {
            var staged=await Run(request,tool,token,extra:["diff","--cached","--no-ext-diff","--no-textconv","--"]);
            if(staged.ExitCode!=0)throw new IOException("Could not review staged changes.");
            var head=await Run(request,tool,token,extra:["rev-parse","--verify","HEAD"]);
            var config=await Run(request,tool,token,extra:["config","--null","--list"]);
            if(config.ExitCode!=0)throw new IOException("Could not fingerprint repository configuration.");
            identity="\nSTAGED CHANGES:\n"+staged.Output+"\nHEAD: "+(head.ExitCode==0?head.Output.Trim():"(unborn)")+"\nRepository configuration SHA256: "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config.Output)));
            if(request.Operation is "fetch" or "push")
            {
                var remote=await Run(request,tool,token,extra:request.Operation=="push"?["remote","get-url","--push","--all","origin"]:["remote","get-url","--all","origin"]);
                if(remote.ExitCode!=0)throw new IOException("Configure an origin remote in your existing Git client first.");
                foreach(var raw in remote.Output.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
                {
                    var url=raw;
                    if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "ssh")url=uri.GetComponents(UriComponents.SchemeAndServer|UriComponents.Path,UriFormat.Unescaped);
                    identity+="\nConfigured origin destination (credentials omitted): "+url;
                }
            }
        }
        if(request.Provider==SourceProvider.Subversion)
        {
            var info=await Run(request,tool,token,extra:["info","--xml","."]);
            if(info.ExitCode!=0)throw new IOException("Could not fingerprint Subversion working-copy identity.");
            using var reader=XmlReader.Create(new StringReader(info.Output),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});var xml=XDocument.Load(reader);
            foreach(var element in xml.Descendants()){var attributes=element.Attributes().OrderBy(a=>a.Name.ToString(),StringComparer.Ordinal).Select(a=>new XAttribute(a)).ToArray();element.RemoveAttributes();element.Add(attributes);}
            identity="\nSubversion working-copy identity SHA256: "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting))));
        }
        string file="";
        if(request.Operation is "stage" or "unstage" or "commit" or "checkout")
        {var path=Path.Combine(request.Folder,Selected(request));if(File.Exists(path)){if(new FileInfo(path).Length>FileWorkspace.MaximumTransferBytes)throw new IOException("Selected file exceeds 128 MiB.");if(request.Operation is "stage" or "commit"&&ContextFiles.Allowed(path))identity+="\nSelected text content to stage / commit:\n"+workspace.Read(path,token).Content;using var stream=File.OpenRead(path);file=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));}else file="(deleted)";}
        return status.Output+"\n"+diff.Output+"\nSelected file SHA256: "+file+identity;
    }
    public async Task<SourcePlan> Preview(SourceRequest request,CancellationToken token=default)
    {
        if(IsReadOnly(request.Operation))throw new IOException("Read-only operations do not need a mutation plan.");
        var args=Arguments(request);var tool=Tool(request.Provider);var state=await State(request,tool,token);
        using(var binary=File.OpenRead(tool))state+="\nCLI executable SHA256: "+Convert.ToHexString(await SHA256.HashDataAsync(binary,token));
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        return new(request,tool,digest,$"Working folder: {Folder(request)}\nExecutable: {tool}\nArguments: "+string.Join(" ",args.Select(a=>System.Text.Json.JsonSerializer.Serialize(a)))+"\n\n"+state+"\n\nOnly this operation is authorized. Git hooks and signing are disabled; use your native client when repository policy requires them. Filters or credential helpers may run. Check-ins / pushes publish to the configured server. No force, reset, clean, automatic merge, or automatic retry is supported.");
    }
    public async Task<SourceResult> Apply(SourcePlan plan,bool confirmed,bool trustedWorkspace,CancellationToken token=default)
    {
        if(!confirmed||!trustedWorkspace)throw new IOException("Review and explicit workspace trust are required.");
        var current=await Preview(plan.Request,token);
        if(current.Executable!=plan.Executable||current.Fingerprint!=plan.Fingerprint||current.Review!=plan.Review)throw new IOException("Working-copy state changed. Preview again before applying.");
        return await Run(plan.Request,plan.Executable,token);
    }
    private async Task<SourceResult> Run(SourceRequest request,string tool,CancellationToken token,bool unborn=false,IReadOnlyList<string>? extra=null)
    {
        token.ThrowIfCancellationRequested();
        var args=(extra??Arguments(request)).ToList();if(unborn)args.Remove("HEAD");
        var info=new ProcessStartInfo(tool){WorkingDirectory=Folder(request),UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(request.Provider==SourceProvider.Git)
        {
            foreach(var arg in new[]{"--no-pager","--no-optional-locks","-c","core.fsmonitor=false","-c","core.hooksPath=/dev/null","-c","commit.gpgSign=false","-c","maintenance.auto=false","-c","protocol.allow=never","-c","protocol.https.allow=always","-c","protocol.ssh.allow=always","-c","protocol.file.allow=always"})info.ArgumentList.Add(arg);
            info.Environment["GIT_TERMINAL_PROMPT"]="0";info.Environment["GCM_INTERACTIVE"]="Never";
            foreach(var key in info.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)&&k is not ("GIT_TERMINAL_PROMPT")).ToArray())info.Environment.Remove(key);
        }
        if(request.Provider==SourceProvider.Subversion){info.ArgumentList.Add("--non-interactive");info.ArgumentList.Add("--no-auth-cache");}
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException("Could not launch provider client.");process.StandardInput.Close();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        async Task<string> Capture(StreamReader stream)
        {var text=new StringBuilder();var buffer=new char[4096];int count;while((count=await stream.ReadAsync(buffer,timeout.Token))>0){if(text.Length+count>1024*1024)throw new IOException("Output exceeds 1 MiB. Narrow the working copy or selection.");text.Append(buffer,0,count);}return text.ToString();}
        var stdout=Capture(process.StandardOutput);var stderr=Capture(process.StandardError);
        try{await Task.WhenAll(stdout,stderr,process.WaitForExitAsync(timeout.Token));return new(process.ExitCode,(await stdout)+"\n"+(await stderr));}
        catch{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}throw new IOException("Provider operation interrupted or output limit exceeded. Inspect status before retrying; a server may have accepted a request.");}
    }
}

public static class TeamsIntegration
{
    public static ProcessStartInfo Home()
    {
        var desktop=false;
        if(OperatingSystem.IsWindows()){using var protocol=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("msteams");desktop=protocol?.GetValue("URL Protocol")!=null;}
        return new(desktop?"msteams:":"https://teams.microsoft.com"){UseShellExecute=true};
    }
    public static Uri Draft(string recipients,string message)
    {
        var users=recipients.Split(new[]{';',','},StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(users.Length is <1 or >10||users.Any(u=>!MailAddress.TryCreate(u,out var parsed)||parsed.Address!=u))throw new IOException("Enter 1–10 Teams email addresses, separated by commas.");
        if(string.IsNullOrWhiteSpace(message)||message.Length>1500)throw new IOException("Enter a reviewed draft of 1–1,500 characters. Copy longer updates instead.");
        return new("https://teams.microsoft.com/l/chat/0/0?users="+Uri.EscapeDataString(string.Join(',',users))+"&message="+Uri.EscapeDataString(message));
    }
}
