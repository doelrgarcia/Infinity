using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;

namespace LocalAI.Core;
public static class WindowsPrerequisites
{
    public const string VisualCppId="msvc-runtime";
    public static bool VisualCppPresent
    {
        get
        {
            if(!OperatingSystem.IsWindows()||!Environment.Is64BitProcess)return false;
            try{return new[]{"msvcp140.dll","vcruntime140.dll","vcruntime140_1.dll"}.All(name=>{var path=Path.Combine(Environment.SystemDirectory,name);if(!File.Exists(path))return false;var v=FileVersionInfo.GetVersionInfo(path);return new Version(v.FileMajorPart,v.FileMinorPart,v.FileBuildPart,v.FilePrivatePart)>=new Version(14,44,35211,0);});}
            catch(Exception e) when(e is IOException or Win32Exception){return false;}
        }
    }
    public static bool IsKnownRecipe(DependencyPackage p)=>p.Id==VisualCppId&&p.NativeInstaller=="vc-redist-x64"&&p.RequiredFiles.SequenceEqual(new[]{"VC_redist.x64.exe"})&&p.Artifacts.Count==1&&!p.Artifacts[0].Zip&&p.Artifacts[0].Path=="VC_redist.x64.exe"&&p.Artifacts[0].Sha256.Length==64&&Uri.TryCreate(p.Artifacts[0].Url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="download.visualstudio.microsoft.com";
    public static async Task<FileStream> OpenVerifiedInstaller(DependencyPackage p,DependencyInstaller cache,CancellationToken token)
    {
        if(!IsKnownRecipe(p)||!cache.IsInstalled(p))throw new IOException("Native prerequisite cache is not verified.");
        var artifact=p.Artifacts.Single();var path=DependencyInstaller.SafePath(cache.PackagePath(p),artifact.Path);
        var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{if(file.Length!=artifact.Bytes||!Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(artifact.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Native prerequisite checksum changed. Repair the cached package before installing.");return file;}
        catch{file.Dispose();throw;}
    }
    public static async Task<bool> Install(DependencyPackage p,DependencyInstaller cache,IProgress<DependencyProgress>? progress,CancellationToken token)
    {
        if(!OperatingSystem.IsWindows()||!Environment.Is64BitProcess)throw new IOException("This prerequisite requires 64-bit Windows.");
        if(!IsKnownRecipe(p))throw new IOException("Unsupported native prerequisite recipe.");
        if(VisualCppPresent)return false;
        token.ThrowIfCancellationRequested();var artifact=p.Artifacts.Single();var path=DependencyInstaller.SafePath(cache.PackagePath(p),artifact.Path);
        // Hold the verified executable read-only through launch; a cached receipt alone is not an elevation trust check.
        using var file=await OpenVerifiedInstaller(p,cache,token);
        if(new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace<200L*1024*1024)throw new IOException("Windows needs at least 200 MiB free for the Visual C++ runtime.");
        var logs=Path.Combine(cache.Root,"logs");Directory.CreateDirectory(logs);var log=Path.Combine(logs,"msvc-"+Guid.NewGuid().ToString("N")+".log");
        var info=new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)!};
        using(var identity=WindowsIdentity.GetCurrent())if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))info.Verb="runas";
        foreach(var arg in new[]{"/install","/quiet","/norestart","/log",log})info.ArgumentList.Add(arg);
        progress?.Report(new(p.Label,"Windows installer running; approval may be required. Cancel waits for this system installer to finish. No restart is performed.",0,0));
        token.ThrowIfCancellationRequested();
        Process? process;try{process=Process.Start(info);}catch(Win32Exception e) when(e.NativeErrorCode==1223){throw new OperationCanceledException("Windows installation approval was cancelled.",e,token);}catch(Win32Exception e){throw new IOException("Windows prerequisite installer could not start: "+e.Message,e);}
        using(process)
        {
            if(process==null)throw new IOException("Windows prerequisite installer could not start.");
            progress?.Report(new(p.Label,"Windows installer started; cancellation waits for completion. No restart is performed.",0,0){ProcessId=process.Id});
            // Do not terminate MSI/Windows servicing when the user cancels a queued download.
            await process.WaitForExitAsync(CancellationToken.None);
            if(process.ExitCode is not (0 or 3010))throw new IOException($"Visual C++ installer failed with exit {process.ExitCode}. Log: {log}");
            if(!VisualCppPresent)throw new IOException($"Visual C++ runtime is not available after installer exit {process.ExitCode}. Log: {log}"+(process.ExitCode==3010?" Windows requested a restart; restart only when you choose.":""));
            progress?.Report(new(p.Label,process.ExitCode==3010?"Installed; Windows recommends a restart, which was not performed.":"Installed and native runtime verified",p.DownloadBytes,p.DownloadBytes));
            token.ThrowIfCancellationRequested();return process.ExitCode==3010;
        }
    }
}
