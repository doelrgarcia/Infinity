using System.Diagnostics;
using System.Text.Json;

namespace LocalAI.Core;
public sealed class DependencyBinding
{
    public string ProfileId {get;set;}="";
    public string ExecutablePackage {get;set;}="";
    public string ExecutablePath {get;set;}="";
    public List<string> ExecutableCompanions {get;set;}=[];
    public string ModelPackage {get;set;}="";
    public string ModelPath {get;set;}="";
    public bool ModelDirectory {get;set;}
    public List<string> ExtraPackages {get;set;}=[];
}
public sealed class DependencyCatalog
{
    public List<DependencyPackage> Packages {get;set;}=[];
    public List<DependencyBinding> Bindings {get;set;}=[];
    public static DependencyCatalog Load()
    {using var stream=typeof(DependencyCatalog).Assembly.GetManifestResourceStream("LocalAI.DependencyCatalog")??throw new IOException("Dependency catalog missing; republish Infinity.");return JsonSerializer.Deserialize<DependencyCatalog>(stream)??throw new IOException("Dependency catalog is empty.");}
}
public sealed record DependencyInspection(IReadOnlyList<DependencyPackage> Missing,IReadOnlyList<string> ReadyProfiles,IReadOnlyList<string> Notices);
public sealed class StartupDependencies : IDisposable
{
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Rymaris","LocalAI","dependencies");
    private readonly Settings settings;
    public DependencyCatalog Catalog {get;}
    public DependencyInstaller Installer {get;}
    private readonly Dictionary<string,string> probeErrors=[];
    private readonly List<string> installationNotices=[];
    private readonly Func<DependencyPackage,bool> nativePresent;
    private static string? FindGit()=> (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Where(d=>!string.IsNullOrWhiteSpace(d)).Select(d=>Path.Combine(d.Trim('"'),"git.exe")).FirstOrDefault(File.Exists);
    private bool ExtraPresent(string id)
    {
        var package=Catalog.Packages.Single(x=>x.Id==id);
        if(package.NativeInstaller!=null)return nativePresent(package);
        if(id!="git-runtime")return Installer.IsInstalled(package);
        if(FindGit()!=null)return true;if(!Installer.IsInstalled(package))return false;
        var directory=Path.GetDirectoryName(DependencyInstaller.SafePath(Installer.PackagePath(package),"cmd/git.exe"))!;Environment.SetEnvironmentVariable("PATH",directory+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"),EnvironmentVariableTarget.Process);return true;
    }
    public StartupDependencies(Settings settings,string? root=null,DependencyCatalog? catalog=null,HttpMessageHandler? testHandler=null,Func<DependencyPackage,bool>? nativePrerequisitePresent=null)
    {this.settings=settings;Catalog=catalog??DependencyCatalog.Load();Installer=new(root??DefaultRoot,testHandler);nativePresent=nativePrerequisitePresent??(p=>WindowsPrerequisites.IsKnownRecipe(p)&&WindowsPrerequisites.VisualCppPresent);}
    private static bool ExecutablePresent(ModelProfile p,DependencyBinding? b)=>File.Exists(p.Executable)&&(b==null||b.ExecutableCompanions.All(f=>File.Exists(Path.Combine(Path.GetDirectoryName(p.Executable)!,f))));
    private bool ModelPresent(ModelProfile p,DependencyBinding? b)
    {
        if(p.IsCloud)return p.Engine is not ("cursor" or "deepseek")||File.Exists(p.ModelPath);
        if(p.Engine=="ryzen")return Directory.Exists(p.ModelPath)&&(b?.ModelDirectory!=true||Catalog.Packages.Single(x=>x.Id==b.ModelPackage).RequiredFiles.All(f=>File.Exists(Path.Combine(p.ModelPath,f))));
        if(!File.Exists(p.ModelPath))return false;try{using var stream=File.OpenRead(p.ModelPath);Span<byte> magic=stackalloc byte[4];return stream.Length>1024&&stream.Read(magic)==4&&magic.SequenceEqual("GGUF"u8);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
    }
    public DependencyInspection Inspect()
    {
        var missing=new Dictionary<string,DependencyPackage>();var ready=new List<string>();var notices=new List<string>(installationNotices);
        foreach(var p in settings.Models)
        {
            if(HybridBackend.Required(p)&&!File.Exists(HybridBackend.DefaultHost)){notices.Add("Infinity's bundled hybrid streaming helper is missing. Republish or reinstall the complete app; existing models and tools are preserved.");continue;}
            var b=Catalog.Bindings.FirstOrDefault(b=>b.ProfileId==p.Id);
            if(b!=null)
            {
                var executable=Catalog.Packages.Single(x=>x.Id==b.ExecutablePackage);
                if(!ExecutablePresent(p,b)&&Installer.IsInstalled(executable))p.Executable=DependencyInstaller.SafePath(Installer.PackagePath(executable),b.ExecutablePath);
                if(b.ModelPackage!="")
                {
                    var model=Catalog.Packages.Single(x=>x.Id==b.ModelPackage);
                    if(!ModelPresent(p,b)&&Installer.IsInstalled(model))p.ModelPath=b.ModelDirectory?Installer.PackagePath(model):DependencyInstaller.SafePath(Installer.PackagePath(model),b.ModelPath);
                    if(!ModelPresent(p,b))missing.TryAdd(model.Id,model);
                }
                if(!ExecutablePresent(p,b))missing.TryAdd(executable.Id,executable);
                foreach(var id in b.ExtraPackages)if(!ExtraPresent(id))missing.TryAdd(id,Catalog.Packages.Single(x=>x.Id==id));
            }
            if(probeErrors.TryGetValue(p.Id,out var probeError))notices.Add(p.Label+": "+probeError);
            else if(ExecutablePresent(p,b)&&ModelPresent(p,b)&&(b==null||b.ExtraPackages.All(ExtraPresent)))ready.Add(p.Id);
            else if(b==null)notices.Add(p.Label+": no automatic recipe for this custom profile. Configure its installed paths in appsettings.json.");
        }
        return new(missing.Values.OrderBy(p=>p.DownloadBytes).ToArray(),ready,notices);
    }
    public IReadOnlyList<DependencyPackage> RequiredForSelection(IEnumerable<DependencyPackage> selected)
    {
        var requested=selected.ToArray();var ids=requested.Select(p=>p.Id).ToHashSet();var extras=Catalog.Bindings.Where(b=>ids.Contains(b.ExecutablePackage)||ids.Contains(b.ModelPackage)).SelectMany(b=>b.ExtraPackages).Distinct().Where(id=>!ExtraPresent(id)).Select(id=>Catalog.Packages.Single(p=>p.Id==id));return extras.Concat(requested).DistinctBy(p=>p.Id).ToArray();
    }
    public async Task CheckTools(CancellationToken token)
    {
        probeErrors.Clear();var available=Inspect().ReadyProfiles;
        foreach(var p in settings.Models.Where(p=>p.IsCloud&&available.Contains(p.Id)))
        {
            token.ThrowIfCancellationRequested();try{await ProbeCli(p,token);}catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}catch(Exception e) when(e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception){probeErrors[p.Id]="Installed tool did not pass its version check. Check publisher prerequisites and use Recheck. "+(e is OperationCanceledException?"Version check timed out.":e.Message);}
        }
    }
    public async Task InstallPackage(DependencyPackage package,IProgress<DependencyProgress>? progress,CancellationToken token,bool force=false)
    {
        await Installer.Install(package,progress,token,force);
        if(package.NativeInstaller!=null&&await WindowsPrerequisites.Install(package,Installer,progress,token))installationNotices.Add("Microsoft Visual C++ installed. Windows recommends a restart; no restart was performed. Save work and restart when you choose.");
    }
    public static async Task ProbeCli(ModelProfile p,CancellationToken token)
    {
        if(!p.IsCloud)return;
        var info=p.Engine=="deepseek"?DeepSeekBridge.CreateStart(p,Path.Combine(Path.GetTempPath(),"Infinity-deepseek-probe"),true):new ProcessStartInfo(p.Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};if(p.Engine=="cursor")info.ArgumentList.Add(p.ModelPath);if(p.Engine!="deepseek")info.ArgumentList.Add("--version");
        using var process=new Process{StartInfo=info};if(!process.Start())throw new IOException("Installed CLI could not start.");
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            async Task<string> Bounded(StreamReader reader){var result=new System.Text.StringBuilder();var buffer=new char[2048];int count;while((count=await reader.ReadAsync(buffer,timeout.Token))>0){if(result.Length+count>32768){timeout.Cancel();throw new IOException("CLI version output exceeded its safe limit.");}result.Append(buffer,0,count);}return result.ToString();}
            var stdout=Bounded(process.StandardOutput);var stderr=Bounded(process.StandardError);await Task.WhenAll(stdout,stderr,process.WaitForExitAsync(timeout.Token));if(process.ExitCode!=0||string.IsNullOrWhiteSpace(await stdout))throw new IOException("CLI version check failed. Repair its installation, then retry.");
        }
        catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}throw;}
    }
    public void Dispose()=>Installer.Dispose();
}
