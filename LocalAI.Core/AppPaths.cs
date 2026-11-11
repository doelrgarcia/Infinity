using System.IO;
using System.Text.Json;

namespace LocalAI.Core;

// The normal data root is %LOCALAPPDATA%\Rymaris\LocalAI. On machines or
// sessions where that directory cannot be written, keep Agent/project and
// conversation state in a portable folder beside the checkout so the app can
// still create projects and chats. Normal users keep the existing location.
public static class AppPaths
{
    public static string NormalRoot {get;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Rymaris","LocalAI");
    public static string PortableRoot {get;}=ResolvePortableRoot();
    public static string DataRoot {get;}=ResolveDataRoot();
    public static string ConversationRoot {get;}=Path.Combine(DataRoot,"conversations");
    public static string AgentRoot {get;}=Path.Combine(DataRoot,"agent");

    private static string ResolvePortableRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(Directory.Exists(Path.Combine(dir.FullName,".git"))||File.Exists(Path.Combine(dir.FullName,"LocalAI.sln")))return Path.Combine(dir.FullName,"userdata");
        return Path.Combine(Path.GetTempPath(),"Infinity","LocalAI","userdata");
    }

    private static string ResolveDataRoot()
    {
        // An update launched from a normal application folder can retain an
        // existing portable store without moving or merging any user data.
        var configuration=Path.Combine(AppContext.BaseDirectory,"appsettings.json");
        if(File.Exists(configuration))
        {
            using var document=JsonDocument.Parse(File.ReadAllText(configuration));
            var entries=document.RootElement.EnumerateObject().Where(p=>p.Name.Equals("UserDataRoot",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(entries.Length>1||entries.Length==1&&entries[0].Name!="UserDataRoot")throw new IOException("Use one UserDataRoot configuration property.");
            if(entries.Length==1&&entries[0].Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))throw new IOException("UserDataRoot must be a full local folder path.");
            var configured=entries.Length==1&&entries[0].Value.ValueKind==JsonValueKind.String?entries[0].Value.GetString():null;
            if(!string.IsNullOrWhiteSpace(configured))
            {
                if(!Path.IsPathFullyQualified(configured)||configured.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("UserDataRoot must be a full local folder path.");
                var root=Path.GetFullPath(configured);
                if(!Directory.Exists(root))throw new IOException("Configured UserDataRoot is unavailable; existing user data was not moved.");
                for(var part=root;part!=null;part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked UserDataRoot paths are excluded.");
                foreach(var child in new[]{"conversations","agent"})
                {
                    var path=Path.Combine(root,child);
                    if((Directory.Exists(path)||File.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked user data stores are excluded.");
                }
                if(!CanWrite(root,"conversations")||!CanWrite(root,"agent"))throw new IOException("Configured UserDataRoot is not writable; no fallback or migration was performed.");
                return root;
            }
        }
        if(CanWrite(NormalRoot,"conversations")&&CanWrite(NormalRoot,"agent"))return NormalRoot;
        if(CanWrite(PortableRoot,"conversations")&&CanWrite(PortableRoot,"agent"))return PortableRoot;
        return NormalRoot;
    }

    private static bool CanWrite(string root,string child)
    {
        try
        {
            var directory=Path.Combine(root,child);
            Directory.CreateDirectory(directory);
            var probe=Path.Combine(directory,".write-probe-"+Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe,"");
            File.Delete(probe);
            return true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException){return false;}
    }
}
