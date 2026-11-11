namespace LocalAI.Core;

public static class CopilotBridge
{
    // Prefer the native launcher. Never execute a PATH .cmd/.ps1 through a shell.
    public static string? FindExecutable(IEnumerable<string>? directories=null)
    {
        directories??=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WinGet","Links")}
            .Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));
        foreach(var directory in directories.Where(d=>!string.IsNullOrWhiteSpace(d)))
        {
            try{var path=Path.GetFullPath(Path.Combine(directory.Trim('"'),OperatingSystem.IsWindows()?"copilot.exe":"copilot"));if(Path.IsPathRooted(directory.Trim('"'))&&File.Exists(path))return path;}
            catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException){}
        }
        return null;
    }
    public static void AddProvider(Settings settings,Func<string?>? discover=null)
    {
        // Existing configuration is authoritative, including a deliberately configured path.
        if(settings.Models.Any(m=>m.Engine=="copilot"))return;
        var id="cloud-copilot";for(var i=2;settings.Models.Any(m=>m.Id.Equals(id,StringComparison.OrdinalIgnoreCase));i++)id="cloud-copilot-"+i;
        settings.Models.Add(new(){Id=id,Label="GitHub Copilot · CLI sign-in",Engine="copilot",Executable=(discover??(()=>FindExecutable()))()??"",ContextLimit=131072});
    }
}
