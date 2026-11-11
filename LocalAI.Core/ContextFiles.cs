using System.Text;
using System.Text.RegularExpressions;

namespace LocalAI.Core;
public sealed record ContextFile(string Path, string Content);
public static partial class ContextFiles
{
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase) {".cs",".cshtml",".razor",".js",".ts",".tsx",".jsx",".sql",".html",".css",".md",".txt",".xml",".csproj",".props",".targets",".xaml",".py",".json",".yaml",".yml",".toml",".csv",".svg",".go",".rs",".java",".cpp",".c",".h",".swift",".plist",".pbxproj",".xcconfig",".xcscheme",".xcworkspacedata",".entitlements",".strings",".xcstrings"};
    private static readonly HashSet<string> Excluded=new(StringComparer.OrdinalIgnoreCase) {".git",".svn","$tf",".tf",".vs",".idea",".localai-backups",".infinity-recovery","bin","obj","node_modules","packages","publish","artifacts","dist","coverage","ReleaseSecrets"};
    public static bool Allowed(string path) => Extensions.Contains(System.IO.Path.GetExtension(path)) && !path.Split(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar).Any(p=>Excluded.Contains(p))
        && !System.IO.Path.GetFileName(path).Contains("secret",StringComparison.OrdinalIgnoreCase) && !System.IO.Path.GetFileName(path).Contains("credential",StringComparison.OrdinalIgnoreCase)
        && !System.IO.Path.GetFileName(path).StartsWith("appsettings",StringComparison.OrdinalIgnoreCase);
    [GeneratedRegex(@"(?i)(password|pwd|api[_-]?key|client[_-]?secret|connectionstring)\s*[=:]\s*[""']?[^\s""';]{3,}|-----BEGIN .*PRIVATE KEY-----|\bsk-[A-Za-z0-9_-]{16,}")]
    private static partial Regex Secrets();
    public static bool ContainsSensitiveContent(string content)=>content.Contains('\0')||Secrets().IsMatch(content);
    public static IEnumerable<string> Discover(string folder,CancellationToken token=default)
    {
        var stack=new Stack<string>(); stack.Push(folder); var count=0;
        while(stack.TryPop(out var dir))
        {
            token.ThrowIfCancellationRequested();
            if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0) continue;
            foreach(var file in Directory.EnumerateFiles(dir)) {token.ThrowIfCancellationRequested();if(Allowed(file)) { if(++count>500) throw new IOException("Folder exceeds 500 eligible files. Select a smaller folder."); yield return file; }}
            foreach(var child in Directory.EnumerateDirectories(dir)) {token.ThrowIfCancellationRequested();if(!Excluded.Contains(System.IO.Path.GetFileName(child))) stack.Push(child);}
        }
    }
    public static ContextFile Read(string path,CancellationToken token=default,int maximumBytes=128*1024)
    {
        token.ThrowIfCancellationRequested();
        path=System.IO.Path.GetFullPath(path);
        if(!Allowed(path)) throw new IOException("Only source/text files are allowed; configuration, credentials and generated folders are excluded.");
        for(var p=path; p!=null; p=System.IO.Path.GetDirectoryName(p)) if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Symbolic links and reparse points are excluded.");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(maximumBytes is <1 or >16*1024*1024)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if(stream.Length>maximumBytes) throw new IOException($"File exceeds {maximumBytes/1024:N0} KiB; use paged chat reads for large source files.");
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);
        var content=reader.ReadToEnd();
        token.ThrowIfCancellationRequested();
        if(content.Contains('\0') || Secrets().IsMatch(content)) throw new IOException("Potential credential or binary content detected; file excluded. Use a sanitized source copy.");
        return new(path,content);
    }
    public static string Format(IEnumerable<ContextFile> files)=>string.Join("\n\n",files.Select(f=>$"SOURCE FILE: {f.Path}\n<untrusted_source>\n{f.Content}\n</untrusted_source>"));
}
