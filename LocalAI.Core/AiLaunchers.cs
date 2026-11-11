using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

namespace LocalAI.Core;
public static class AiLaunchers
{
    public static string? CodexDesktopInstallation()
    {
        if(!OperatingSystem.IsWindows())return null;
        try
        {
            using var packages=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if(packages==null)return null;
            foreach(var name in packages.GetSubKeyNames().Where(n=>n.StartsWith("OpenAI.Codex_",StringComparison.Ordinal)&&n.EndsWith("__2p2nqsd0c76g0",StringComparison.Ordinal)).OrderDescending())
            {using var package=packages.OpenSubKey(name);if(package?.GetValue("PackageRootFolder") is string root&&CodexExecutable(root) is { } exe)return exe;}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        return null;
    }
    public static string? CodexExecutable(string root)
    {
        try
        {
            using var xml=XmlReader.Create(Path.Combine(root,"AppxManifest.xml"),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});var doc=XDocument.Load(xml);
            if(doc.Descendants().FirstOrDefault(x=>x.Name.LocalName=="Identity")?.Attribute("Name")?.Value!="OpenAI.Codex")return null;
            var app=doc.Descendants().FirstOrDefault(x=>x.Name.LocalName=="Application"&&x.Attribute("Id")?.Value=="App");var relative=app?.Attribute("Executable")?.Value;
            if(relative==null||Path.IsPathRooted(relative))return null;
            var path=Path.GetFullPath(Path.Combine(root,relative));return path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(path)?path:null;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or XmlException or ArgumentException){return null;}
    }
    public static string Website(string provider)=>provider switch{"cursor"=>"https://cursor.com","grok"=>"https://grok.com","codex"=>"https://chatgpt.com","deepseek"=>"https://chat.deepseek.com","copilot"=>"https://github.com/copilot",_=>throw new ArgumentException("Unknown provider")};
    public static string? DeepSeekInstallation()
    {
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+"/Programs",Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
        {var path=Path.Combine(root,"DeepSeek Harness","DeepSeek Harness.exe");if(File.Exists(path))return path;}
        return null;
    }
    public static string? DeepSeekCliPath()
    {
        if(DeepSeekInstallation() is { } desktop)
        {var bundled=Path.Combine(Path.GetDirectoryName(desktop)!,"resources","runtime","cli","bin","dsh.cmd");if(File.Exists(bundled))return bundled;}
        var npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","dsh.cmd");
        return File.Exists(npm)?npm:null;
    }
    public static ProcessStartInfo DeepSeekDownloadStart()=>new("https://www.deepseek.com/en/download/"){UseShellExecute=true};
    public static string? DesktopPath(string provider)
    {
        if(provider=="copilot")return null;
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path=provider switch{"cursor"=>Path.Combine(local,"Programs","cursor","Cursor.exe"),"grok"=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Grok Bot","Grok Bot.exe"),"codex"=>CodexDesktopInstallation(),"deepseek"=>DeepSeekInstallation(),_=>throw new ArgumentException("Unknown provider")};
        return path!=null&&File.Exists(path)?path:null;
    }
    public static ProcessStartInfo WebsiteStart(string provider)=>new(Website(provider)){UseShellExecute=true};
    public static ProcessStartInfo DesktopStart(string provider,Func<string?>? codexInstallation=null)
    {
        if(provider=="codex")
        {if((codexInstallation??CodexDesktopInstallation)()==null)return WebsiteStart(provider);var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")){UseShellExecute=false};info.ArgumentList.Add("shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App");return info;}
        return DesktopPath(provider) is { } path?new(path){UseShellExecute=true}:provider=="deepseek"?DeepSeekDownloadStart():WebsiteStart(provider);
    }
    public static ProcessStartInfo CliStart(string provider,string launcherScript,ModelProfile? resolvedProfile=null,Func<string?>? deepSeekCli=null)
    {
        if(provider is not ("cursor" or "grok" or "codex" or "deepseek" or "copilot"))throw new ArgumentException("Unknown provider");
        string executable;var arguments=new List<string>();
        if(provider=="deepseek")
        {executable=(deepSeekCli??DeepSeekCliPath)()??throw new FileNotFoundException("DeepSeek CLI is not installed. Install DeepSeek Harness using its official download page, then reopen this panel.");arguments.AddRange(["web","--host","127.0.0.1","--port","0"]);}
        else
        {
            resolvedProfile??=Settings.Load(Path.Combine(Path.GetDirectoryName(launcherScript)!,"appsettings.json")).Models.FirstOrDefault(p=>p.Engine==provider);
            if(resolvedProfile==null||resolvedProfile.Engine!=provider||!File.Exists(resolvedProfile.Executable))throw new IOException("Resolved CLI installation is unavailable.");
            executable=resolvedProfile.Executable;
            if(provider=="cursor"){if(!File.Exists(resolvedProfile.ModelPath))throw new IOException("Cursor CLI script is unavailable.");arguments.Add(resolvedProfile.ModelPath);}
        }
        var workspace=Path.Combine(AppPaths.DataRoot,"cli-bridge",provider);Directory.CreateDirectory(workspace);
        // Let Windows open the native console tool (or the publisher's .cmd launcher).
        // No PowerShell host, unsigned .ps1, execution-policy change, or command interpolation.
        var info=new ProcessStartInfo(executable){UseShellExecute=true,WorkingDirectory=workspace};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        return info;
    }
}
