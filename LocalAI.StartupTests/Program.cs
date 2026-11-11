using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAI.Core;

internal static class Program
{
    static readonly Dictionary<string,object> Results=[];
    static string Scratch="";
    static void Check(string name,bool value){Results[name]=value;if(!value)throw new Exception("Assertion failed: "+name);}
    static async Task Reject(string name,Func<Task> run){try{await run();}catch(Exception e) when(e is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException){Check(name,true);return;}Check(name,false);}
    static byte[] Model(){var b=new byte[4096];Encoding.ASCII.GetBytes("GGUF").CopyTo(b,0);return b;}
    static DependencyPackage Package(byte[] data,bool zip=false,string id="fixture",string file="model.gguf")=>new(){Id=id,Label=id,Version="1",InstalledBytes=4096,RequiredFiles=[file],Artifacts=[new(){Url="https://huggingface.co/infinity-test/resolve/pinned/"+file,Bytes=data.Length,Sha256=Convert.ToHexString(SHA256.HashData(data)),Path=file,Zip=zip}]};
    static byte[] Zip(params (string name,string value,int attributes)[] files){using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))foreach(var f in files){var e=zip.CreateEntry(f.name);e.ExternalAttributes=f.attributes;using var writer=new StreamWriter(e.Open());writer.Write(f.value);}return stream.ToArray();}
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return run(request,token);}
    }
    static HttpResponseMessage Reply(byte[] b,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new ByteArrayContent(b)};
    static Handler Full(byte[] b)=>new((_,_)=>Task.FromResult(Reply(b)));
    sealed class InterruptedStream(byte[] data,CancellationTokenSource cancel):MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){token.ThrowIfCancellationRequested();if(Position>=100){cancel.Cancel();token.ThrowIfCancellationRequested();}return base.ReadAsync(buffer[..Math.Min(buffer.Length,100)],token);}
    }
    static string Root(string name)=>Path.Combine(Scratch,name);
    static async Task Fixtures()
    {
        var b=Model();var p=Package(b);var h=Full(b);using(var i=new DependencyInstaller(Root("basic"),h))
        {
            Check("missing before install",!i.IsInstalled(p));await i.Install(p,null,default);Check("verified atomic install",i.IsInstalled(p)&&File.ReadAllBytes(Path.Combine(i.PackagePath(p),"model.gguf")).SequenceEqual(b));
            await i.Install(p,null,default);Check("installed package does not redownload",h.Calls==1);File.Delete(Path.Combine(i.PackagePath(p),"model.gguf"));Check("missing installed file detected",!i.IsInstalled(p));await i.Install(p,null,default);Check("damaged installation repaired",i.IsInstalled(p));Check("previous installation preserved",Directory.GetDirectories(Path.GetDirectoryName(i.PackagePath(p))!,"*.previous-*").Length==1);
            var payload=Path.Combine(i.PackagePath(p),"model.gguf");File.WriteAllBytes(payload,new byte[b.Length]);Check("same length damage requires explicit repair",i.IsInstalled(p));var calls=h.Calls;await i.Install(p,null,default,true);Check("forced repair redownloads and restores exact pinned bytes",h.Calls==calls+1&&File.ReadAllBytes(payload).SequenceEqual(b));Check("forced repair preserves superseded installation",Directory.GetDirectories(Path.GetDirectoryName(i.PackagePath(p))!,"*.previous-*").Length==2);
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();await Reject("cancelled forced repair refuses replacement",()=>i.Install(p,null,cancelled.Token,true));Check("cancelled repair preserves working package",File.ReadAllBytes(payload).SequenceEqual(b));}
            var receipt=Path.Combine(i.PackagePath(p),".infinity-installed.json");File.WriteAllText(receipt,"{\"Id\":\"fixture\",\"Version\":\"1\",\"Files\":null}");Check("malformed receipt rejected",!i.IsInstalled(p));
        }
        using(var i=new DependencyInstaller(Root("bad-hash"),Full(new byte[b.Length]))) {await Reject("checksum failure rejected",()=>i.Install(p,null,default));Check("checksum failure never promoted",!Directory.Exists(i.PackagePath(p)));}
        using(var i=new DependencyInstaller(Root("bad-size"),Full(new byte[3])))await Reject("pinned length failure rejected",()=>i.Install(p,null,default));
        var retries=0;h=new((_,_)=>Task.FromResult(++retries<3?Reply([],HttpStatusCode.ServiceUnavailable):Reply(b)));using(var i=new DependencyInstaller(Root("retry"),h)){await i.Install(p,null,default);Check("transient HTTP retries bounded and recover",h.Calls==3&&i.IsInstalled(p));}
        h=new((_,_)=>Task.FromResult(Reply([],HttpStatusCode.ServiceUnavailable)));using(var i=new DependencyInstaller(Root("retry-fail"),h)){await Reject("publisher failure surfaces after retry",()=>i.Install(p,null,default));Check("maximum three HTTP attempts",h.Calls==3);}
        foreach(var resume in new[]{true,false})
        {
            var received=false;h=new((request,_)=>{received=request.Headers.Range?.Ranges.Single().From==100;var r=Reply(resume?b[100..]:b,resume?HttpStatusCode.PartialContent:HttpStatusCode.OK);if(resume)r.Content.Headers.ContentRange=new ContentRangeHeaderValue(100,b.Length-1,b.Length);return Task.FromResult(r);});
            using var i=new DependencyInstaller(Root("resume-"+resume),h);var stage=i.StagingPath(p);Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(i.Root,".infinity-cache"),"Infinity dependency cache v1");File.WriteAllBytes(Path.Combine(stage,"model.gguf.partial"),b[..100]);await i.Install(p,null,default);Check(resume?"206 resume verified":"200 restart verified",received&&i.IsInstalled(p));
        }
        h=new((_,_)=>{var r=Reply(b,HttpStatusCode.PartialContent);r.Content.Headers.ContentRange=new ContentRangeHeaderValue(1,b.Length,b.Length+1);return Task.FromResult(r);});using(var i=new DependencyInstaller(Root("wrong-range"),h))await Reject("incorrect resume range rejected",()=>i.Install(p,null,default));
        using(var cancel=new CancellationTokenSource()){h=new(async(_,token)=>{cancel.Cancel();await Task.Delay(100,token);return Reply(b);});using var i=new DependencyInstaller(Root("cancel"),h);await Reject("cancellation propagated",()=>i.Install(p,null,cancel.Token));Check("cancelled installation never promoted",!Directory.Exists(i.PackagePath(p)));}
        var partialRoot=Root("stream-cancel");using(var cancel=new CancellationTokenSource())
        {h=new((_,_)=>{var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new InterruptedStream(b,cancel))};r.Content.Headers.ContentLength=b.Length;return Task.FromResult(r);});using var i=new DependencyInstaller(partialRoot,h);await Reject("cancel during transfer propagated",()=>i.Install(p,null,cancel.Token));Check("cancel keeps resumable partial bytes",new FileInfo(Path.Combine(i.StagingPath(p),"model.gguf.partial")).Length==100);}
        h=new((request,_)=>{var offset=request.Headers.Range?.Ranges.Single().From??0;var r=Reply(b[(int)offset..],HttpStatusCode.PartialContent);r.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,b.Length-1,b.Length);return Task.FromResult(r);});using(var i=new DependencyInstaller(partialRoot,h)){await i.Install(p,null,default);Check("cancelled transfer resumes to verified install",i.IsInstalled(p));}
        var lockRoot=Root("concurrent");Directory.CreateDirectory(Path.Combine(lockRoot,"locks"));File.WriteAllText(Path.Combine(lockRoot,".infinity-cache"),"Infinity dependency cache v1");using(var lease=new FileStream(Path.Combine(lockRoot,"locks/fixture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var i=new DependencyInstaller(lockRoot,Full(b)))await Reject("concurrent package install refused",()=>i.Install(p,null,default));
        foreach(var entry in new[]{"../escaped.exe","C:/escaped.exe","file.txt:evil","/absolute.exe"})
        {var zip=Zip((entry,"bad",0));var z=Package(zip,true,file:"tool.exe");using var i=new DependencyInstaller(Root("unsafe-"+Guid.NewGuid().ToString("N")),Full(zip));await Reject("archive path refused "+entry,()=>i.Install(z,null,default));}
        foreach(var kind in new[]{"link","duplicate"})
        {var zip=kind=="link"?Zip(("tool.exe","target",unchecked((int)0xa0000000))):Zip(("tool.exe","a",0),("TOOL.exe","b",0));using var i=new DependencyInstaller(Root(kind),Full(zip));await Reject("archive "+kind+" refused",()=>i.Install(Package(zip,true,file:"tool.exe"),null,default));}
        var valid=Zip(("bin/tool.exe","verified",0));using(var i=new DependencyInstaller(Root("zip-good"),Full(valid))){var z=Package(valid,true,file:"bin/tool.exe");await i.Install(z,null,default);Check("verified ZIP extraction",i.IsInstalled(z)&&File.ReadAllText(Path.Combine(i.PackagePath(z),"bin/tool.exe"))=="verified");}
        var unowned=Root("unowned");Directory.CreateDirectory(unowned);File.WriteAllText(Path.Combine(unowned,"keep.txt"),"user data");using(var i=new DependencyInstaller(unowned,Full(b))){await Reject("unowned cache refused",()=>i.Install(p,null,default));Check("unowned data preserved",File.ReadAllText(Path.Combine(unowned,"keep.txt"))=="user data");}
        foreach(var url in new[]{"http://huggingface.co/test","https://127.0.0.1/test","https://evil.example/test","https://huggingface.co.evil.example/test"})await Reject("source refused "+url,()=>{DependencyInstaller.ValidateSource(url);return Task.CompletedTask;});
        h=new((_,_)=>{var r=Reply([],HttpStatusCode.Redirect);r.Headers.Location=new Uri("https://evil.example/file");return Task.FromResult(r);});using(var i=new DependencyInstaller(Root("redirect"),h))await Reject("redirect publisher revalidated",()=>i.Install(p,null,default));
        var blob=Package(b,id:"git-blob");blob.Artifacts[0].Sha256="";blob.Artifacts[0].GitBlobSha1=Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"blob {b.Length}\0").Concat(b).ToArray()));using(var i=new DependencyInstaller(Root("blob"),Full(b))){await i.Install(blob,null,default);Check("pinned Git blob verified",i.IsInstalled(blob));}
        var catalog=new DependencyCatalog{Packages=[p],Bindings=[new(){ProfileId="local",ExecutablePackage=p.Id,ExecutablePath="model.gguf",ModelPackage=p.Id,ModelPath="model.gguf"}]};var settings=new Settings{Models=[new(){Id="local",Executable="missing.exe",ModelPath="missing.gguf"},new(){Id="custom",Executable="custom-missing.exe",ModelPath="custom-missing.gguf"}]};var original=JsonSerializer.Serialize(settings);
        using(var setup=new StartupDependencies(settings,Root("resolve"),catalog,Full(b))){Check("missing packages deduplicated",setup.Inspect().Missing.Count==1);Check("custom profile has actionable notice",setup.Inspect().Notices.Count==1);Check("inspection performs no downloads",original==JsonSerializer.Serialize(settings));await setup.Installer.Install(p,null,default);var report=setup.Inspect();Check("managed paths resolved in memory",report.ReadyProfiles.SequenceEqual(new[]{"local"})&&settings.Models[0].Executable.StartsWith(setup.Installer.Root));Check("custom paths preserved",settings.Models[1].Executable=="custom-missing.exe");}
        var pinned=DependencyCatalog.Load();Check("six managed profiles have pinned recipes",pinned.Bindings.Count==6);Check("all catalog artifacts pinned",pinned.Packages.All(x=>x.Artifacts.All(a=>a.Bytes>0&&(a.Sha256.Length==64||a.GitBlobSha1.Length==40))));foreach(var a in pinned.Packages.SelectMany(x=>x.Artifacts))DependencyInstaller.ValidateSource(a.Url);Check("catalog publisher sources validated",true);
        Check("legacy package fingerprints omit unused native installer field",!JsonSerializer.Serialize(p).Contains("NativeInstaller"));
        using(var nativeSetup=new StartupDependencies(new Settings{Models=[new(){Id="qwen25-coder",Engine="llama",Executable="missing",ModelPath="missing"}]},Root("native-support"),nativePrerequisitePresent:_=>false))
        {Check("local runtime selection adds Visual C++ prerequisite first",nativeSetup.RequiredForSelection([pinned.Packages.Single(p=>p.Id=="llama-vulkan")]).Select(p=>p.Id).SequenceEqual(new[]{"msvc-runtime","llama-vulkan"}));Check("inspection detects missing native prerequisite without installing",nativeSetup.Inspect().Missing.Any(p=>p.Id=="msvc-runtime"));}
        var nativePackage=new DependencyPackage{Id="msvc-runtime",Version="fixture",NativeInstaller="vc-redist-x64",RequiredFiles=["VC_redist.x64.exe"],InstalledBytes=b.Length,Artifacts=[new(){Url="https://download.visualstudio.microsoft.com/fixture/VC_redist.x64.exe",Path="VC_redist.x64.exe",Sha256=Convert.ToHexString(SHA256.HashData(b)),Bytes=b.Length}]};
        using(var nativeCache=new DependencyInstaller(Root("native-hash"),Full(b)))
        {await nativeCache.Install(nativePackage,null,default);using(var held=await WindowsPrerequisites.OpenVerifiedInstaller(nativePackage,nativeCache,default))Check("native prerequisite executable hash verified before elevation",held.Length==b.Length);var nativePath=DependencyInstaller.SafePath(nativeCache.PackagePath(nativePackage),"VC_redist.x64.exe");var changed=b.ToArray();changed[^1]^=1;File.WriteAllBytes(nativePath,changed);Check("size-only native receipt cannot detect tampering",nativeCache.IsInstalled(nativePackage));await Reject("native prerequisite same-length tampering refused before elevation",async()=>{using var held=await WindowsPrerequisites.OpenVerifiedInstaller(nativePackage,nativeCache,default);});}
        nativePackage.NativeInstaller="arbitrary-installer";using(var nativeCache=new DependencyInstaller(Root("native-unknown"),Full(b)))await Reject("unknown native installer recipe refused",()=>nativeCache.Install(nativePackage,null,default));
        var desktopRoot=Root("codex-desktop");Directory.CreateDirectory(desktopRoot);File.WriteAllText(Path.Combine(desktopRoot,"ChatGPT.exe"),"fixture");File.WriteAllText(Path.Combine(desktopRoot,"AppxManifest.xml"),"<Package><Identity Name='OpenAI.Codex'/><Applications><Application Id='App' Executable='ChatGPT.exe'/></Applications></Package>");
        Check("Codex registered manifest resolves executable",AiLaunchers.CodexExecutable(desktopRoot)==Path.Combine(desktopRoot,"ChatGPT.exe"));Check("absent Codex desktop falls back to official website",AiLaunchers.DesktopStart("codex",()=>null).FileName=="https://chatgpt.com");Check("installed Codex uses package activation",AiLaunchers.DesktopStart("codex",()=>Path.Combine(desktopRoot,"ChatGPT.exe")).ArgumentList.Single()==@"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App");File.WriteAllText(Path.Combine(desktopRoot,"AppxManifest.xml"),"<!DOCTYPE p [<!ENTITY ext SYSTEM 'file:///C:/secret'>]><Package>&ext;</Package>");Check("Codex manifest external entities refused",AiLaunchers.CodexExecutable(desktopRoot)==null);
        var priorPath=Environment.GetEnvironmentVariable("PATH");try{Environment.SetEnvironmentVariable("PATH","");using var setup=new StartupDependencies(new Settings{Models=[new(){Id="cloud-cursor",Engine="cursor",Executable="missing",ModelPath="missing"}]},Root("support"));var selected=setup.RequiredForSelection([pinned.Packages.Single(p=>p.Id=="cursor-cli")]);Check("Cursor selection includes Git prerequisite first",selected.Select(p=>p.Id).SequenceEqual(new[]{"git-runtime","cursor-cli"}));Check("dependency selection does not alter machine PATH",Environment.GetEnvironmentVariable("PATH")=="");}finally{Environment.SetEnvironmentVariable("PATH",priorPath);}
        var abi=Root("hybrid-abi");Directory.CreateDirectory(abi);File.WriteAllBytes(Path.Combine(abi,"onnxruntime-genai.dll"),b);await Reject("unsupported hybrid native ABI refused",()=>{HybridBackend.VerifyDirectory(new ModelProfile{Executable=Path.Combine(abi,"ryzenai-server.exe")});return Task.CompletedTask;});Check("hybrid correction scoped to known model profile",HybridBackend.Required(new(){Id="llama32-hybrid",Engine="ryzen"})&&!HybridBackend.Required(new(){Id="custom",Engine="ryzen"}));
        await VerifyProcessRestart();
        using(var i=new DependencyInstaller(Root("manifest-stage"),Full(b))){var changed=Package(b);changed.Artifacts[0].Url+="?changed=1";Check("changed manifest cannot reuse stale staging files",i.StagingPath(p)!=i.StagingPath(changed));}
    }
    sealed class CachedArtifacts(string directory):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var name=Path.GetFileName(request.RequestUri!.AbsolutePath);name=name switch{"agent-cli-package.zip"=>"cursor.zip","grok-1.0.70-windows-x86_64.exe"=>"grok.exe",_=>name};var path=Path.Combine(directory,name);if(!File.Exists(path))throw new IOException("Missing publisher artifact fixture: "+name);var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(path))};r.Content.Headers.ContentLength=new FileInfo(path).Length;return Task.FromResult(r);
        }
    }
    static async Task Live(string root,string? cached)
    {
        var catalog=DependencyCatalog.Load();using var installer=new DependencyInstaller(root,cached==null?null:new CachedArtifacts(cached));
        foreach(var id in new[]{"git-runtime","llama-vulkan","codex-cli","cursor-cli","grok-cli","ryzen-runtime"})
        {
            var p=catalog.Packages.Single(x=>x.Id==id);await installer.Install(p,null,default);Check("publisher package installed "+id,installer.IsInstalled(p));
            if(id=="git-runtime")Environment.SetEnvironmentVariable("PATH",Path.Combine(installer.PackagePath(p),"cmd")+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"));
        }
        foreach(var binding in catalog.Bindings.Where(b=>b.ProfileId.StartsWith("cloud-")))
        {
            var package=catalog.Packages.Single(p=>p.Id==binding.ExecutablePackage);var directory=installer.PackagePath(package);var profile=new ModelProfile{Id=binding.ProfileId,Engine=binding.ProfileId.Replace("cloud-",""),Executable=DependencyInstaller.SafePath(directory,binding.ExecutablePath),ModelPath=binding.ModelPath.Length>0?DependencyInstaller.SafePath(directory,binding.ModelPath):""};await StartupDependencies.ProbeCli(profile,default);Check("installed CLI version probe "+binding.ProfileId,true);
        }
        Results["artifactSource"]=cached==null?"publisher HTTPS downloads":"official HTTPS artifacts previously downloaded and pinned; read-only fixture cache";
    }
    private sealed class DownloadProgress(string id):IProgress<DependencyProgress>
    {
        private DateTime last=DateTime.MinValue;
        public void Report(DependencyProgress p){if((DateTime.UtcNow-last).TotalSeconds<5&&p.Completed!=p.Total)return;last=DateTime.UtcNow;Console.WriteLine($"PROGRESS {id} {p.Phase} {p.Completed}/{p.Total}");}
    }
    private sealed class CancelNativeProgress(CancellationTokenSource cancel):IProgress<DependencyProgress>
    {
        public int? StartedProcessId {get;private set;}
        public void Report(DependencyProgress progress){if(progress.ProcessId is { } pid){StartedProcessId=pid;cancel.Cancel();}}
    }
    static async Task DownloadAll(string root,string output)
    {
        var catalog=DependencyCatalog.Load();using var installer=new DependencyInstaller(root);
        Results["artifactSource"]="fresh publisher HTTPS downloads in guest; no host cache";
        void Save()=>File.WriteAllText(output,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var p in catalog.Packages.OrderBy(p=>p.Id=="git-runtime"?0:1).ThenBy(p=>p.DownloadBytes))
        {
            try
            {
                Check("package initially absent "+p.Id,!installer.IsInstalled(p));Save();
                await installer.Install(p,new DownloadProgress(p.Id),default);
                if(p.NativeInstaller!=null)await WindowsPrerequisites.Install(p,installer,new DownloadProgress(p.Id),default);
                Check("publisher package installed "+p.Id,installer.IsInstalled(p)&&(p.NativeInstaller==null||WindowsPrerequisites.VisualCppPresent));
                if(p.Id=="git-runtime")Environment.SetEnvironmentVariable("PATH",Path.Combine(installer.PackagePath(p),"cmd")+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"));
            }
            catch(Exception e){Results["package failed "+p.Id]=e.Message;}
            Save();
        }
        var settings=new Settings{Models=catalog.Bindings.Select((b,i)=>new ModelProfile{Id=b.ProfileId,Label=b.ProfileId,Engine=b.ProfileId.StartsWith("cloud-")?b.ProfileId[6..]:b.ModelDirectory?"ryzen":"llama",Executable="missing-guest-executable",ModelPath="missing-guest-model",Endpoint="http://127.0.0.1:"+(8110+i),ContextLimit=8192}).ToList()};
        using var setup=new StartupDependencies(settings,root);await setup.CheckTools(default);var inspection=setup.Inspect();
        Results["readyProfiles"]=inspection.ReadyProfiles;Results["missingPackages"]=inspection.Missing.Select(p=>p.Id).ToArray();Results["notices"]=inspection.Notices;
        Results["all profiles installed and CLI version checks passed"]=inspection.ReadyProfiles.Count==settings.Models.Count&&inspection.Missing.Count==0&&inspection.Notices.Count==0;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!,"guest-appsettings.json"),JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
        if(Results.Keys.Any(k=>k.StartsWith("package failed "))||inspection.ReadyProfiles.Count!=settings.Models.Count)Results["error"]="Not all guest installations or version checks passed; see individual results.";
        Save();
    }
    static async Task RuntimeSmoke(string config,string output)
    {
        var profile=Settings.Load(config).Models.Single(p=>p.Id=="qwen25-coder");profile.Endpoint="http://127.0.0.1:8128";profile.ContextLimit=1024;
        var info=new ProcessStartInfo(profile.Executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(profile.Executable)!,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-m",profile.ModelPath,"--host","127.0.0.1","--port","8128","-ngl","0","-c","1024","--parallel","1","--alias",profile.Id})info.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=info};using var client=new LocalClient();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        Task<string>? stdout=null,stderr=null;var started=false;Results["runtime mode"]="CPU-only installation smoke; no GPU/NPU claim";
        try
        {
            Check("local runtime process started",started=process.Start());stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();
            File.WriteAllText(output,JsonSerializer.Serialize(Results));
            while(!await client.Healthy(profile,timeout.Token))
            {if(process.HasExited){Results["runtime exit code"]=process.ExitCode;throw new IOException("Native runtime exited before readiness: "+process.ExitCode);}await Task.Delay(500,timeout.Token);}
            Check("runtime identity and model path verified",true);
            var answer=await client.Generate(profile,[new("user","What is 3 + 4? Respond only with the digit 7.")],24,0,_=>{},timeout.Token);Results["synthetic arithmetic response"]=answer.Text;Check("downloaded model inference returns seven",answer.Text.Trim()=="7");
        }
        finally
        {
            if(started&&!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}
            if(stdout!=null){var text=await stdout;File.WriteAllText(Path.ChangeExtension(output,".stdout.txt"),text[..Math.Min(text.Length,2_000_000)]);}
            if(stderr!=null){var text=await stderr;File.WriteAllText(Path.ChangeExtension(output,".stderr.txt"),text[..Math.Min(text.Length,2_000_000)]);}
        }
    }
    static async Task Managed(string root,string config)
    {
        var before=SHA256.HashData(File.ReadAllBytes(config));var settings=Settings.Load(config);settings.Models=settings.Models.Where(p=>DependencyCatalog.Load().Bindings.Any(b=>b.ProfileId==p.Id)).ToList();
        foreach(var p in settings.Models){p.Executable="missing-managed-test";if(p.Engine=="cursor")p.ModelPath="missing-managed-test";}
        using var setup=new StartupDependencies(settings,root);await setup.CheckTools(default);Check("downloaded tools resolve all configured profiles",setup.Inspect().ReadyProfiles.Count==settings.Models.Count);
        foreach(var p in settings.Models)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));InferenceResult answer;
            if(p.IsCloud){answer=await new CliAssistant().Generate(p,[new("user","What is 3 + 4? Respond only with the digit 7. Do not use tools, inspect files, or run commands.")],_=>{},timeout.Token);Check("managed CLI answers "+p.Id,answer.Text.Trim()=="7");var launch=AiLaunchers.CliStart(p.Engine,Path.Combine(Path.GetDirectoryName(config)!,"launch-provider.ps1"),p);Check("interactive launcher receives managed path "+p.Id,launch.FileName==p.Executable);}
            else
            {
                var port=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);port.Start();p.Endpoint="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port;port.Stop();using var client=new LocalClient();using var runtime=new RuntimeManager();await runtime.Ensure(p,client,timeout.Token);answer=await client.Generate(p,[new("user","What is 3 + 4? Respond only with the digit 7.")],32,0,_=>{},timeout.Token);Results["synthetic arithmetic response "+p.Id]=answer.Text;Results["model quality "+p.Id]=new{exactRequestedDigit=answer.Text.Trim()=="7",response=answer.Text,answer.FinishReason};Check("managed runtime streams completed response "+p.Id,answer.Text.Length>0);Check("managed runtime exact arithmetic answer "+p.Id,answer.Text.Trim()=="7");
            }
        }
        Check("managed workflow preserves configuration file",before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(config))));
    }
    static async Task VerifyProcessRestart()
    {
        var directory=Root("process-restart");Directory.CreateDirectory(directory);
        foreach(var stage in new[]{"write","read"})
        {
            var executable=Environment.ProcessPath??throw new IOException("QA process executable unavailable");var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach(var argument in new[]{"--persistence-child",stage,directory})info.ArgumentList.Add(argument);
            using var child=Process.Start(info)??throw new IOException("Persistence child could not start");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await child.WaitForExitAsync(timeout.Token);}catch{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}throw;}
            if(child.ExitCode!=0)throw new IOException("Persistence child failed: "+stage);
        }
        var writePid=File.ReadAllText(Path.Combine(directory,"write.pid"));var readPid=File.ReadAllText(Path.Combine(directory,"read.pid"));
        Check("conversation survives distinct writer and reader processes",writePid!=readPid&&writePid!=Environment.ProcessId.ToString()&&readPid!=Environment.ProcessId.ToString());
    }
    static async Task<int> Main(string[] args)
    {
        if(args.FirstOrDefault()=="--persistence-child")
        {
            var stage=args[1];var directory=Path.GetFullPath(args[2]);var store=new ConversationStore(directory);
            if(stage=="write")store.Save(new Conversation{Id=Guid.Parse("71770e60-bc38-4d70-a26d-4c451942cab5"),Title="Restart fixture",ModelId="local",TaskBrief="Preserve the task",ResponseStyle="Plan",AutoCliFallback=false,FallbackProviderId="cloud-grok",Messages=[new("user","3 + 4?"),new("assistant","7","cloud-codex")]});
            else if(stage=="read")
            {
                var chats=store.Load(out var warning);if(warning.Length!=0||chats.Count!=1)return 2;var c=chats.Single();
                if(c.Id!=Guid.Parse("71770e60-bc38-4d70-a26d-4c451942cab5")||c.Title!="Restart fixture"||c.ModelId!="local"||c.TaskBrief!="Preserve the task"||c.ResponseStyle!="Plan"||c.AutoCliFallback||c.FallbackProviderId!="cloud-grok"||c.Messages.Count!=2||c.Messages[0]!=new Message("user","3 + 4?")||c.Messages[1]!=new Message("assistant","7","cloud-codex"))return 3;
            }
            else return 4;
            File.WriteAllText(Path.Combine(directory,stage+".pid"),Environment.ProcessId.ToString());return 0;
        }

        if(args.FirstOrDefault()=="--profile-qa")
        {
            var outputPath=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);var config=Path.GetFullPath(args[2]);var before=SHA256.HashData(File.ReadAllBytes(config));
            try
            {
                var settings=Settings.Load(config);var profile=settings.Models.Single(p=>p.Id==args[3]);using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));InferenceResult answer;
                if(profile.IsCloud)answer=await new CliAssistant().Generate(profile,[new("user","What is 3 + 4? Respond only with the digit 7. Do not use tools, inspect files, or run commands.")],_=>{},timeout.Token);
                else
                {
                    var port=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);port.Start();profile.Endpoint="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port;port.Stop();var overrideIndex=Array.IndexOf(args,"--hybrid-host");using var client=new LocalClient();using var runtime=new RuntimeManager(overrideIndex>=0?Path.GetFullPath(args[overrideIndex+1]):null);await runtime.Ensure(profile,client,timeout.Token);Check("production runtime manager verifies identity and readiness",await client.Healthy(profile,timeout.Token));Results["ownedProcessId"]=runtime.OwnedProcessId??0;Results["runtimeLog"]=runtime.LastLogPath??"";File.WriteAllText(outputPath,JsonSerializer.Serialize(Results));if(args.Contains("--measure"))await Task.Delay(3000,timeout.Token);
                    answer=await client.Generate(profile,[new("user","What is 3 + 4? Respond only with the digit 7.")],32,0,_=>{},timeout.Token);if(args.Contains("--measure")){for(var repetition=0;repetition<15;repetition++){answer=await client.Generate(profile,[new("user","What is 3 + 4? Respond only with the digit 7.")],32,0,_=>{},timeout.Token);if(answer.Text.Trim()!="7")throw new IOException("Repeated hardware measurement inference failed arithmetic.");}Results["measurementRequests"]=16;await Task.Delay(3000,timeout.Token);}if(args.Contains("--limit-qa")){var limited=await client.Generate(profile,[new("user","List the integers from 1 to 100, separated by spaces. Do not stop early.")],4,0,_=>{},timeout.Token);Results["outputLimitResponse"]=limited.Text;Check("real runtime output limit is reported",limited.FinishReason=="length"&&limited.Text.Length>0);}
                }
                Results["response"]=answer.Text;Check("exact synthetic arithmetic answer",answer.Text.Trim()=="7");Check("configuration bytes preserved",before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(config))));
            }
            catch(Exception e){Results["error"]=e.ToString();}
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);File.WriteAllText(outputPath,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));return Results.ContainsKey("error")?1:0;
        }
        if(args.FirstOrDefault()=="--native-cancel-qa")
        {
            var outputPath=Path.GetFullPath(args[1]);
            try
            {
                if(!Environment.UserName.Equals("WDAGUtilityAccount",StringComparison.OrdinalIgnoreCase)||!File.Exists(@"C:\Results\guest-identity.txt"))throw new IOException("Actual native cancellation QA is restricted to the disposable Windows Sandbox guest.");
                Check("native prerequisite initially absent",!WindowsPrerequisites.VisualCppPresent);using var cache=new DependencyInstaller(Path.GetFullPath(args[2]));var package=DependencyCatalog.Load().Packages.Single(p=>p.Id==WindowsPrerequisites.VisualCppId);await cache.Install(package,null,default);
                using var cancellation=new CancellationTokenSource();var progress=new CancelNativeProgress(cancellation);var cancelled=false;try{await WindowsPrerequisites.Install(package,cache,progress,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
                Check("cancel requested after actual native process start",progress.StartedProcessId.HasValue&&cancellation.IsCancellationRequested);Check("active native installer finishes safely before cancellation returns",cancelled&&WindowsPrerequisites.VisualCppPresent);
                var exited=false;try{using var process=Process.GetProcessById(progress.StartedProcessId!.Value);exited=process.HasExited;}catch(ArgumentException){exited=true;}Check("owned native installer has exited",exited);Results["nativeProcessId"]=progress.StartedProcessId??0;
            }
            catch(Exception e){Results["error"]=e.ToString();}
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);File.WriteAllText(outputPath,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));return Results.ContainsKey("error")?1:0;
        }
        if(args.FirstOrDefault()=="--cursor-stdin-qa")
        {
            var outputPath=Path.GetFullPath(args[1]);var root=Path.Combine(Path.GetTempPath(),"Infinity-cursor-protocol-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var script=Path.Combine(root,"probe.cjs");await File.WriteAllTextAsync(script,"process.stdout.write(JSON.stringify(process.argv.slice(2)))");var prompt=string.Concat(Enumerable.Repeat("\"\\测试🙂\n--force ",1000));
                var profile=new ModelProfile{Engine="cursor",Executable=Path.GetFullPath(args[2]),ModelPath=script,TrustBridgeWorkspace=true};var info=CliAssistant.CreateStart(profile,root,"unused",prompt);
                Check("Cursor prompt is absent from native command arguments",!info.ArgumentList.Contains(prompt)&&info.ArgumentList.Sum(s=>s.Length)<4000);
                using var process=Process.Start(info)??throw new IOException("Node fixture did not start");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.StandardInput.WriteAsync(prompt);process.StandardInput.Close();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));try{await process.WaitForExitAsync(deadline.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
                var received=JsonSerializer.Deserialize<string[]>(await stdout)??[];Check("Cursor Unicode quoting and large prompt roundtrip through stdin",process.ExitCode==0&&received.Last()==prompt);Check("Cursor retains ask mode and no automatic trust",received.Contains("ask")&&!received.Contains("--trust")&&!received.Contains("--force"));Check("Cursor protocol fixture has no diagnostic failure",string.IsNullOrWhiteSpace(await stderr));
                async Task<(int Exit,string Text)> Probe(string value)
                {
                    using var child=Process.Start(CliAssistant.CreateStart(profile,root,"unused",value))??throw new IOException("Node fixture did not start");var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
                    try{await child.StandardInput.WriteAsync(value);child.StandardInput.Close();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(20));await child.WaitForExitAsync(limit.Token);await errors;return(child.ExitCode,await output);}catch{if(!child.HasExited)child.Kill(true);throw;}
                }
                var full=new string('测',CliAssistant.MaximumPromptCharacters);var fullResult=await Probe(full);Check("Cursor accepts the entire maximum coding request including multibyte UTF8",fullResult.Exit==0&&JsonSerializer.Deserialize<string[]>(fullResult.Text)!.Last()==full);
                var overflow=await Probe(new string('x',CliAssistant.MaximumPromptUtf8Bytes+1));Check("Cursor byte overflow never invokes the provider script",overflow.Exit!=0&&overflow.Text.Length==0);

            }
            catch(Exception e){Results["error"]=e.ToString();}
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);File.WriteAllText(outputPath,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));return Results.ContainsKey("error")?1:0;
        }
        var runtimeSmoke=args.FirstOrDefault()=="--runtime-smoke";var all=args.FirstOrDefault()=="--download-all";var live=args.FirstOrDefault()=="--live";var inspect=args.FirstOrDefault()=="--inspect";var managed=args.FirstOrDefault()=="--managed-qa";var output=Path.GetFullPath(args.ElementAtOrDefault(runtimeSmoke||all||live||inspect||managed?1:0)??"startup-tests.json");Scratch=Path.Combine(Path.GetTempPath(),"Infinity-startup-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Scratch);
        if(runtimeSmoke){Directory.CreateDirectory(Path.GetDirectoryName(output)!);try{await RuntimeSmoke(Path.GetFullPath(args[2]),output);}catch(Exception e){Results["error"]=e.ToString();}File.WriteAllText(output,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));return Results.ContainsKey("error")?1:0;}
        if(all){Directory.CreateDirectory(Path.GetDirectoryName(output)!);try{await DownloadAll(Path.GetFullPath(args[2]),output);}catch(Exception e){Results["error"]=e.ToString();}File.WriteAllText(output,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));return Results.ContainsKey("error")?1:0;}
        try{if(live)await Live(Path.GetFullPath(args[2]),args.ElementAtOrDefault(3));else if(managed)await Managed(Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));else if(inspect){var path=Path.GetFullPath(args[2]);var before=SHA256.HashData(File.ReadAllBytes(path));var settings=Settings.Load(path);using var setup=new StartupDependencies(settings);await setup.CheckTools(default);var report=setup.Inspect();Results["readyProfiles"]=report.ReadyProfiles;Results["missingPackages"]=report.Missing.Select(p=>p.Id).ToArray();Results["notices"]=report.Notices;Check("existing configuration unchanged",before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));Check("all configured existing profiles available",report.ReadyProfiles.Count==settings.Models.Count&&report.Missing.Count==0&&report.Notices.Count==0);}else await Fixtures();}
        catch(Exception e){Results["error"]=e.ToString();}
        Results["scratch"]=Scratch;Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllText(output,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(Results));return Results.ContainsKey("error")?1:0;
    }
}
