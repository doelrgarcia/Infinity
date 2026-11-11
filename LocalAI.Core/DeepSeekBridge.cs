using System.Diagnostics;
using System.Text;

namespace LocalAI.Core;
public static class DeepSeekBridge
{
    public static void AddProvider(Settings settings)
    {
        if(settings.Models.Any(m=>m.Engine=="deepseek"))return;
        var desktop=AiLaunchers.DeepSeekInstallation();
        var id="cloud-deepseek";for(var i=2;settings.Models.Any(m=>m.Id.Equals(id,StringComparison.OrdinalIgnoreCase));i++)id="cloud-deepseek-"+i;
        settings.Models.Add(new(){Id=id,Label="DeepSeek · Harness sign-in",Engine="deepseek",Executable=desktop??"",ModelPath=desktop==null?"":Path.Combine(Path.GetDirectoryName(desktop)!,"resources","app.asar"),ContextLimit=131072});
    }
    public static ProcessStartInfo CreateStart(ModelProfile profile,string workspace,bool probe=false)
    {
        if(!File.Exists(profile.Executable)||!File.Exists(profile.ModelPath))throw new IOException("DeepSeek Harness is unavailable. Install or repair the existing app, then reopen Infinity.");
        Directory.CreateDirectory(workspace);byte[] source;
        using(var resource=typeof(DeepSeekBridge).Assembly.GetManifestResourceStream("LocalAI.DeepSeekBridge")??throw new IOException("DeepSeek bridge is missing; republish Infinity."))
        using(var buffer=new MemoryStream()){resource.CopyTo(buffer);source=buffer.ToArray();}
        var script=Path.Combine(workspace,"deepseek-bridge-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source))[..16]+".cjs");
        try{using var output=new FileStream(script,FileMode.CreateNew,FileAccess.Write,FileShare.Read);output.Write(source);}
        catch(IOException) when(File.Exists(script)){if(File.GetAttributes(script).HasFlag(FileAttributes.ReparsePoint)||!File.ReadAllBytes(script).SequenceEqual(source))throw new IOException("The cached DeepSeek bridge changed. Inspect it before retrying.");}
        var home=Environment.GetEnvironmentVariable("DSH_HOME");if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dsh");else if(home=="~"||home.StartsWith("~/",StringComparison.Ordinal)||home.StartsWith("~\\",StringComparison.Ordinal))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),home.Length==1?"":home[2..]);home=Path.GetFullPath(home);
        var info=new ProcessStartInfo(profile.Executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workspace,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
        foreach(var key in info.Environment.Keys.Where(k=>k.StartsWith("DSH_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DEEPSEEK_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("NODE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("ELECTRON_",StringComparison.OrdinalIgnoreCase)).ToArray())info.Environment.Remove(key);
        info.Environment["ELECTRON_RUN_AS_NODE"]="1";
        foreach(var arg in new[]{"--expose-internals",script,profile.ModelPath,home,probe?"probe":"generate"})info.ArgumentList.Add(arg);
        return info;
    }
}
