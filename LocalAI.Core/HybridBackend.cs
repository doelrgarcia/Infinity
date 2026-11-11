using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalAI.Core;
public static class HybridBackend
{
    public const string OgaSha256="C1C2E889338E82E7CE1118A22DBC2908E628633E13F0AE25F2B44D0A0A9E1227";
    public static bool Required(ModelProfile profile)=>profile.Engine=="ryzen"&&profile.Id=="llama32-hybrid";
    public static string DefaultHost=>ResolveHost(Path.Combine(AppContext.BaseDirectory,"Native","Infinity.RyzenHost.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Infinity"));
    public static string ResolveHost(string bundled,string installation)
    {
        try
        {
            var selectorPath=Path.Combine(installation,"Start.cmd");if(new FileInfo(selectorPath).Length>4096||(File.GetAttributes(selectorPath)&FileAttributes.ReparsePoint)!=0)return bundled;
            var selector=File.ReadAllText(selectorPath);
            var matches=Regex.Matches(selector,@"releases\\([a-f0-9]{40})\\LocalAI\.Desktop\.exe",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(matches.Count!=1)return bundled;
            var revision=matches[0].Groups[1].Value;var folder=Path.GetFullPath(Path.Combine(installation,"releases",revision,"Native"));
            for(var part=folder;part!=null;part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)return bundled;
            var installed=Path.Combine(folder,"Infinity.RyzenHost.exe");
            if(Path.GetFullPath(installed).Equals(Path.GetFullPath(bundled),StringComparison.OrdinalIgnoreCase))return bundled;
            var manifestPath=Path.Combine(folder,"backend-build.json");if(new FileInfo(manifestPath).Length>65536||(File.GetAttributes(manifestPath)&FileAttributes.ReparsePoint)!=0)return bundled;
            using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));var root=manifest.RootElement;
            if(root.GetProperty("sourceRevision").GetString()!=revision||root.GetProperty("patch").GetString()!="sequence-growth-v2")return bundled;
            if(new FileInfo(installed).Length>64*1024*1024||(File.GetAttributes(installed)&FileAttributes.ReparsePoint)!=0)return bundled;
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed)));
            if(!hash.Equals(root.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))return bundled;
            // Reuse only the identical helper from the user's active installation.
            // Its installed location can have required Windows/NPU access that a
            // development copy lacks. A newer/different bundled helper wins.
            if(File.Exists(bundled)&&(new FileInfo(bundled).Length>64*1024*1024||!hash.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundled))),StringComparison.OrdinalIgnoreCase)))return bundled;
            return installed;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or RegexMatchTimeoutException){return bundled;}
    }
    public static string VerifyDirectory(ModelProfile profile)
    {
        var directory=Path.GetFullPath(Path.GetDirectoryName(profile.Executable)??throw new IOException("Hybrid backend directory is missing."));
        if(directory.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Hybrid native libraries must be installed on a local disk.");
        var path=Path.Combine(directory,"onnxruntime-genai.dll");using var file=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(file)).Equals(OgaSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Hybrid native ABI differs from the verified Ryzen 1.7 runtime. Install or repair its managed package.");
        return directory;
    }
}
