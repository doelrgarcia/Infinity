using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalAI.Core;
public sealed class DependencyArtifact
{
    public string Url {get;set;}="";
    public string Sha256 {get;set;}="";
    public string GitBlobSha1 {get;set;}="";
    public long Bytes {get;set;}
    public string Path {get;set;}="";
    public bool Zip {get;set;}
}
public sealed class DependencyPackage
{
    public string Id {get;set;}="";
    public string Label {get;set;}="";
    public string Version {get;set;}="";
    public string LicenseUrl {get;set;}="";
    public long InstalledBytes {get;set;}
    public List<string> RequiredFiles {get;set;}=[];
    public List<DependencyArtifact> Artifacts {get;set;}=[];
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeInstaller {get;set;}
    public long DownloadBytes=>Artifacts.Sum(a=>a.Bytes);
}
public sealed record DependencyProgress(string Package,string Phase,long Completed,long Total)
{
    public int? ProcessId {get;init;}
}
public sealed class DependencyInstaller : IDisposable
{
    private const string Owner="Infinity dependency cache v1";
    private readonly HttpClient http;
    public string Root {get;}
    public DependencyInstaller(string root,HttpMessageHandler? testHandler=null)
    {
        Root=System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        if(Root==System.IO.Path.GetPathRoot(Root)?.TrimEnd(System.IO.Path.DirectorySeparatorChar))throw new IOException("Choose a dedicated dependency cache folder, not a drive root.");
        var handler=testHandler??PublicHandler();http=new(handler){Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("Infinity-DependencySetup/1.0");
    }
    private static SocketsHttpHandler PublicHandler()
    {
        var handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(20)};
        handler.ConnectCallback=async(c,token)=>{var addresses=await Dns.GetHostAddressesAsync(c.DnsEndPoint.Host,token);if(addresses.Length==0||addresses.Any(a=>!WebResearch.IsPublicAddress(a)))throw new HttpRequestException("Dependency downloads cannot connect to private/reserved addresses.");foreach(var a in addresses){var socket=new Socket(a.AddressFamily,SocketType.Stream,ProtocolType.Tcp);try{await socket.ConnectAsync(a,c.DnsEndPoint.Port,token);return new NetworkStream(socket,true);}catch{socket.Dispose();}}throw new HttpRequestException("Could not connect to the dependency publisher.");};return handler;
    }
    public static Uri ValidateSource(string value)
    {
        var uri=WebResearch.ValidateUrl(value);var h=uri.Host;
        if(!new[]{"github.com","githubusercontent.com","huggingface.co","hf.co","xethub.hf.co","cursor.com","x.ai","storage.googleapis.com","download.visualstudio.microsoft.com"}.Any(domain=>h.Equals(domain,StringComparison.OrdinalIgnoreCase)||h.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase)))throw new IOException("Dependency source is not an approved publisher host.");
        return uri;
    }
    private static void ValidatePackage(DependencyPackage p)
    {
        if(!Regex.IsMatch(p.Id,"^[a-z0-9][a-z0-9-]{0,79}$")||!Regex.IsMatch(p.Version,"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")||p.Artifacts.Count==0||p.RequiredFiles.Count==0)throw new IOException("Invalid dependency manifest.");
        foreach(var a in p.Artifacts){ValidateSource(a.Url);if(a.Bytes<1||a.Bytes>100L*1024*1024*1024||!(Regex.IsMatch(a.Sha256,"^[a-fA-F0-9]{64}$")||Regex.IsMatch(a.GitBlobSha1,"^[a-fA-F0-9]{40}$")))throw new IOException("Dependency manifest needs a pinned hash and size.");}
        if(p.NativeInstaller!=null&&!WindowsPrerequisites.IsKnownRecipe(p))throw new IOException("Unsupported native prerequisite recipe.");
    }
    public string PackagePath(DependencyPackage p)=>SafePath(Root,"packages/"+p.Id+"/"+p.Version);
    public string StagingPath(DependencyPackage p)=>SafePath(Root,"staging/"+p.Id+"-"+p.Version+"-"+Fingerprint(p)[..16]);
    public static string SafePath(string directory,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||System.IO.Path.IsPathRooted(relative)||relative.Contains(':')||relative.Replace('\\','/').Split('/').Any(x=>x==".."))throw new IOException("Unsafe dependency archive path.");
        var basePath=System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar);var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath,relative));
        if(!path.StartsWith(basePath+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Dependency path escaped its cache.");
        for(var current=path;current!=null;current=System.IO.Path.GetDirectoryName(current)){if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked dependency cache paths are refused.");}
        return path;
    }
    private bool Owned(){var marker=SafePath(Root,".infinity-cache");return File.Exists(marker)&&new FileInfo(marker).Length==Encoding.UTF8.GetByteCount(Owner)&&File.ReadAllText(marker)==Owner;}
    private void Initialize()
    {
        if(!Owned())
        {
            if(Directory.Exists(Root)&&Directory.EnumerateFileSystemEntries(Root).Any())throw new IOException("The dependency cache folder contains unowned data. Choose a dedicated empty folder.");
            Directory.CreateDirectory(Root);File.WriteAllText(SafePath(Root,".infinity-cache"),Owner);
        }
    }
    private sealed record Receipt(string Id,string Version,string Manifest,Dictionary<string,long> Files);
    private static string Fingerprint(DependencyPackage p)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(p)));
    private static bool Complete(string directory,DependencyPackage p)
    {
        try
        {
            var marker=SafePath(directory,".infinity-installed.json");if(!File.Exists(marker)||new FileInfo(marker).Length>8*1024*1024)return false;
            var receipt=JsonSerializer.Deserialize<Receipt>(File.ReadAllText(marker));return receipt!=null&&receipt.Id==p.Id&&receipt.Version==p.Version&&receipt.Manifest==Fingerprint(p)&&receipt.Files is {Count:>0}&&p.RequiredFiles.All(f=>receipt.Files.ContainsKey(f.Replace('\\','/')))&&receipt.Files.All(f=>File.Exists(SafePath(directory,f.Key))&&new FileInfo(SafePath(directory,f.Key)).Length==f.Value);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
    public bool IsInstalled(DependencyPackage p)=>Owned()&&Complete(PackagePath(p),p);
    private async Task<HttpResponseMessage> Open(DependencyArtifact artifact,long offset,CancellationToken token)
    {
        var uri=ValidateSource(artifact.Url);
        for(var n=0;n<8;n++)
        {
            ValidateSource(uri.AbsoluteUri);using var request=new HttpRequestMessage(HttpMethod.Get,uri);if(offset>0)request.Headers.Range=new RangeHeaderValue(offset,null);
            using var headerTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);headerTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            HttpResponseMessage r;try{r=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,headerTimeout.Token);}catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new HttpRequestException("Dependency publisher did not respond within 45 seconds. Retry when connected.");}
            if((int)r.StatusCode is >=300 and <400){var next=r.Headers.Location; r.Dispose();uri=next==null?throw new IOException("Publisher redirect has no destination."):new Uri(uri,next);continue;}
            if(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden){r.Dispose();throw new IOException("Publisher requires account/license access. Complete access with the publisher, then retry; no credentials were copied.");}
            if(!r.IsSuccessStatusCode){var status=(int)r.StatusCode;r.Dispose();throw new HttpRequestException($"Dependency publisher returned HTTP {status}. Retry when available.");}return r;
        }
        throw new IOException("Dependency download exceeded its redirect limit.");
    }
    private static async Task<bool> Verify(string path,DependencyArtifact a,CancellationToken token)
    {
        if(!File.Exists(path)||new FileInfo(path).Length!=a.Bytes)return false;
        using var stream=File.OpenRead(path);string hash;
        if(a.Sha256.Length>0)hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
        else {using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);digest.AppendData(Encoding.UTF8.GetBytes($"blob {a.Bytes}\0"));var buffer=new byte[1024*1024];int count;while((count=await stream.ReadAsync(buffer,token))>0)digest.AppendData(buffer,0,count);hash=Convert.ToHexString(digest.GetHashAndReset());}
        return hash.Equals(a.Sha256.Length>0?a.Sha256:a.GitBlobSha1,StringComparison.OrdinalIgnoreCase);
    }
    private async Task Download(DependencyPackage p,DependencyArtifact a,string target,IProgress<DependencyProgress>? progress,CancellationToken token)
    {
        for(var attempt=0;;attempt++)
        {
            try{await DownloadAttempt(p,a,target,progress,token);return;}
            catch(HttpRequestException) when(attempt<2&&!token.IsCancellationRequested){progress?.Report(new(p.Label,$"Connection interrupted; retrying ({attempt+1}/2)",0,a.Bytes));await Task.Delay(TimeSpan.FromSeconds(attempt+1),token);}
        }
    }
    private async Task DownloadAttempt(DependencyPackage p,DependencyArtifact a,string target,IProgress<DependencyProgress>? progress,CancellationToken token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);if(await Verify(target,a,token))return;
        var partial=target+".partial";SafePath(Root,System.IO.Path.GetRelativePath(Root,partial));var offset=File.Exists(partial)?new FileInfo(partial).Length:0;
        if(offset>a.Bytes){File.Delete(partial);offset=0;}
        if(offset<a.Bytes)
        {
            using var response=await Open(a,offset,token);
            if(response.StatusCode==HttpStatusCode.PartialContent){var range=response.Content.Headers.ContentRange;if(range?.From!=offset||range.Length!=a.Bytes)throw new IOException("Publisher returned an unexpected resume range.");}
            else offset=0;
            if(response.Content.Headers.ContentLength is long length&&length!=a.Bytes-offset)throw new IOException("Publisher download size differs from the pinned manifest.");
            using var input=await response.Content.ReadAsStreamAsync(token);using var output=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read,1024*1024,true);output.SetLength(offset);output.Position=offset;
            var buffer=new byte[1024*1024];int read;var last=DateTime.MinValue;
            while(true)
            {
                using var idle=CancellationTokenSource.CreateLinkedTokenSource(token);idle.CancelAfter(TimeSpan.FromSeconds(60));
                try{read=await input.ReadAsync(buffer,idle.Token);}catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new HttpRequestException("Dependency download stalled for 60 seconds.");}catch(IOException e){throw new HttpRequestException("Dependency connection was interrupted.",e);}
                if(read==0)break;if(output.Position+read>a.Bytes)throw new IOException("Dependency download exceeds its pinned size.");await output.WriteAsync(buffer.AsMemory(0,read),token);if((DateTime.UtcNow-last).TotalMilliseconds>100){progress?.Report(new(p.Label,"Downloading",output.Position,a.Bytes));last=DateTime.UtcNow;}
            }
            await output.FlushAsync(token);
        }
        progress?.Report(new(p.Label,"Verifying publisher hash",a.Bytes,a.Bytes));
        if(!await Verify(partial,a,token)){File.Delete(partial);throw new IOException("Dependency checksum/length verification failed. Retry downloads a fresh copy.");}
        File.Move(partial,target,true);
    }
    private static void Extract(string archive,string directory,DependencyPackage p,CancellationToken token)
    {
        using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>100000)throw new IOException("Dependency archive contains too many files.");long expanded=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in zip.Entries)
        {
            token.ThrowIfCancellationRequested();var target=SafePath(directory,e.FullName);
            if(((e.ExternalAttributes>>16)&0xf000)==0xa000||(e.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new IOException("Linked archive entries are refused.");
            expanded=checked(expanded+e.Length);if(expanded>Math.Max(p.InstalledBytes,1)+64*1024*1024)throw new IOException("Dependency archive exceeds its expansion budget.");
            if(e.FullName.EndsWith('/')||e.FullName.EndsWith('\\')){Directory.CreateDirectory(target);continue;}
            if(!seen.Add(target))throw new IOException("Duplicate dependency archive paths are refused.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);using var from=e.Open();using var to=File.Create(target);var buffer=new byte[1024*1024];int read;long written=0;
            while((read=from.Read(buffer))>0){token.ThrowIfCancellationRequested();written+=read;if(written>e.Length)throw new IOException("Archive entry exceeds its declared size.");to.Write(buffer,0,read);}if(written!=e.Length)throw new IOException("Archive entry is incomplete.");
        }
    }
    public async Task Install(DependencyPackage p,IProgress<DependencyProgress>? progress,CancellationToken token,bool force=false)
    {
        ValidatePackage(p);Initialize();
        var lockFile=SafePath(Root,"locks/"+p.Id+".lock");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lockFile)!);using var lease=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(!force&&IsInstalled(p))return;
        // A forced repair never trusts an old size-only receipt or completed staging tree.
        // Keep the active installation until fresh pinned artifacts are fully verified.
        var stage=force?StagingPath(p)+"-repair-"+Guid.NewGuid().ToString("N"):StagingPath(p);Directory.CreateDirectory(stage);
        if(!Complete(stage,p))
        {
            var targets=new List<(DependencyArtifact Artifact,string Path,bool Verified)>();
            for(var i=0;i<p.Artifacts.Count;i++){var artifact=p.Artifacts[i];var path=SafePath(stage,artifact.Zip?$".download-{i}.zip":artifact.Path);var verified=await Verify(path,artifact,token);if(!verified&&File.Exists(path))File.Delete(path);targets.Add((artifact,path,verified));}
            var downloaded=targets.Sum(x=>x.Verified?x.Artifact.Bytes:File.Exists(x.Path+".partial")?Math.Min(x.Artifact.Bytes,new FileInfo(x.Path+".partial").Length):0);
            var needed=p.DownloadBytes-downloaded+(p.Artifacts.Any(a=>a.Zip)?p.InstalledBytes:0);var drive=new DriveInfo(System.IO.Path.GetPathRoot(Root)!);if(drive.AvailableFreeSpace<needed+256L*1024*1024)throw new IOException($"Not enough free disk space. Setup needs about {needed/1073741824d:F1} GiB plus temporary space.");
            foreach(var item in targets)
            {
                var a=item.Artifact;var destination=item.Path;if(!item.Verified)await Download(p,a,destination,progress,token);
                if(a.Zip){progress?.Report(new(p.Label,"Extracting verified package",0,0));await Task.Run(()=>Extract(destination,stage,p,token),token);File.Delete(destination);}
            }
            token.ThrowIfCancellationRequested();if(!p.RequiredFiles.All(f=>File.Exists(SafePath(stage,f))))throw new IOException("Verified archive is missing required installation files.");
            var files=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories).Where(f=>!f.EndsWith(".partial",StringComparison.Ordinal)&&System.IO.Path.GetFileName(f)!=".infinity-installed.json").ToDictionary(f=>System.IO.Path.GetRelativePath(stage,f).Replace('\\','/'),f=>new FileInfo(f).Length);
            File.WriteAllText(SafePath(stage,".infinity-installed.json"),JsonSerializer.Serialize(new Receipt(p.Id,p.Version,Fingerprint(p),files)));
        }
        token.ThrowIfCancellationRequested();var final=PackagePath(p);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(final)!);var backup=final+".previous-"+Guid.NewGuid().ToString("N");var previous=Directory.Exists(final);
        if(previous)Directory.Move(final,backup);
        try{Directory.Move(stage,final);}catch{if(previous&&!Directory.Exists(final))Directory.Move(backup,final);throw;}
        progress?.Report(new(p.Label,"Installed and verified",p.DownloadBytes,p.DownloadBytes));
    }
    public void Dispose()=>http.Dispose();
}
