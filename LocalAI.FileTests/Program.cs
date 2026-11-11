using System.Runtime.InteropServices;
using System.Text.Json;
using LocalAI.Core;

var output=Path.GetFullPath(args[0]);
var network=args.Length>2&&args[1]=="--network-root";
var parent=network?Path.GetFullPath(args[2]):Path.GetTempPath();
var root=Path.Combine(parent,"Infinity-file-QA-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Console.WriteLine("Owned fixture: "+root);
var rows=new List<object>();
void Assert(bool value){if(!value)throw new IOException("Assertion failed.");}
void Reject(Action action){try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException){return;}throw new IOException("Expected rejection.");}
void Test(string name,Action action){try{action();rows.Add(new{name,passed=true});}catch(Exception e){rows.Add(new{name,passed=false,error=e.Message});}}
var ws=new FileWorkspace([root]);var a=Path.Combine(root,"Alpha.txt");File.WriteAllText(a,"first version");
Test("workspace reads screened source",()=>Assert(ws.Read(a).Content=="first version"));
Test("workspace lists files and folders",()=>{Directory.CreateDirectory(Path.Combine(root,"nested"));Assert(ws.List(root).Any(e=>e.Path==a)&&ws.List(root).Any(e=>e.IsDirectory));});
Test("workspace rejects parent traversal",()=>Reject(()=>ws.Resolve("../outside.txt")));
Test("workspace rejects sibling prefix escape",()=>Reject(()=>ws.Resolve(root+"-outside/file.txt")));
Test("workspace rejects alternate streams",()=>Reject(()=>ws.Resolve(a+":hidden")));
Test("workspace rejects device namespaces",()=>Reject(()=>new FileWorkspace([@"\\?\C:\Windows"])));
Test("workspace rejects metadata aliases",()=>Reject(()=>ws.Resolve(Path.Combine(root,".git.","config"))));
Test("workspace rejects recovery traversal",()=>Reject(()=>ws.Resolve(Path.Combine(root,FileWorkspace.RecoveryFolder,"old.txt"))));
Test("workspace protects its own permission and program state",()=>{var state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Rymaris","LocalAI","conversations","sample.json");Reject(()=>new FileWorkspace([Path.GetPathRoot(state)!]).Resolve(state));var binary=Path.Combine(AppContext.BaseDirectory,"LocalAI.Core.dll");Reject(()=>new FileWorkspace([Path.GetPathRoot(binary)!]).Resolve(binary));});
Test("workspace read detects embedded credentials",()=>{var p=Path.Combine(root,"Sensitive.txt");File.WriteAllText(p,"password = synthetic-value");Reject(()=>ws.Read(p));});
Test("workspace protects portable runtime logs and private state",()=>Reject(()=>new FileWorkspace([Path.GetPathRoot(AppPaths.PortableRoot)!]).Resolve(Path.Combine(AppPaths.PortableRoot,"logs","runtime-private.log"))));
Test("workspace preserves appsettings files",()=>{var p=Path.Combine(root,"appsettings.json");File.WriteAllText(p,"{}");Reject(()=>ws.Preview(new("write",p,Content:"changed")));Reject(()=>ws.Preview(new("delete",p)));Assert(File.ReadAllText(p)=="{}");});
Test("workspace preview does not write",()=>{var p=ws.Preview(new("write",a,Content:"reviewed version"));Assert(p.Before=="first version"&&p.After=="reviewed version"&&File.ReadAllText(a)=="first version");});
Test("workspace requires confirmation",()=>{var p=ws.Preview(new("write",a,Content:"not approved"));Reject(()=>ws.Apply(p,false));Assert(File.ReadAllText(a)=="first version");});
Test("workspace overwrite preserves original backup",()=>{var receipt=ws.Apply(ws.Preview(new("write",a,Content:"reviewed version")),true);Assert(File.ReadAllText(a)=="reviewed version"&&receipt.RecoveryPath!=null&&File.ReadAllText(receipt.RecoveryPath)=="first version");});
Test("workspace rejects same length stale writes",()=>{var p=ws.Preview(new("write",a,Content:"new"));File.WriteAllText(a,"changed! version");Reject(()=>ws.Apply(p,true));Assert(File.ReadAllText(a)=="changed! version");});
Test("workspace cancellation preserves files",()=>{using var stop=new CancellationTokenSource();var p=ws.Preview(new("write",a,Content:"cancelled"));stop.Cancel();try{ws.Apply(p,true,stop.Token);throw new IOException("Cancellation ignored");}catch(OperationCanceledException){}Assert(File.ReadAllText(a)=="changed! version");});
Test("workspace creates new reviewed files",()=>{var p=Path.Combine(root,"New.py");ws.Apply(ws.Preview(new("write",p,Content:"print(7)")),true);Assert(ws.Read(p).Content=="print(7)");});
Test("workspace refuses appeared destinations",()=>{var p=Path.Combine(root,"Appeared.txt");var plan=ws.Preview(new("write",p,Content:"proposed"));File.WriteAllText(p,"external");Reject(()=>ws.Apply(plan,true));Assert(File.ReadAllText(p)=="external");});
Test("workspace creates reviewed folders",()=>{var p=Path.Combine(root,"New folder");ws.Apply(ws.Preview(new("mkdir",p)),true);Assert(Directory.Exists(p));});
Test("workspace verified copy matches source",()=>{var dest=Path.Combine(root,"Copy.txt");ws.Apply(ws.Preview(new("copy",a,dest)),true);Assert(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(dest)));});
Test("workspace copy refuses existing destination",()=>Reject(()=>ws.Preview(new("copy",a,Path.Combine(root,"Copy.txt")))));
Test("workspace copy rejects stale sources",()=>{var plan=ws.Preview(new("copy",a,Path.Combine(root,"StaleCopy.txt")));File.AppendAllText(a,"!");Reject(()=>ws.Apply(plan,true));Assert(!File.Exists(plan.Destination));});
Test("workspace move preserves recovery bytes",()=>{var dest=Path.Combine(root,"Renamed.txt");var content=File.ReadAllText(a);var receipt=ws.Apply(ws.Preview(new("move",a,dest)),true);Assert(!File.Exists(a)&&File.ReadAllText(dest)==content&&receipt.RecoveryPath!=null&&File.ReadAllText(receipt.RecoveryPath)==content);});
Test("workspace delete is recoverable",()=>{var p=Path.Combine(root,"Copy.txt");var content=File.ReadAllText(p);var receipt=ws.Apply(ws.Preview(new("delete",p)),true);Assert(!File.Exists(p)&&File.ReadAllText(receipt.RecoveryPath!)==content);});
Test("workspace recovery can restore a reviewed file",()=>{var item=ws.ListRecovery(root).Single(e=>e.Path.EndsWith("-Copy.txt"));var target=Path.Combine(root,"Restored.txt");ws.Apply(ws.Preview(new("restore",item.Path,target)),true);Assert(File.Exists(target)&&File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(item.Path)));});
Test("workspace recovery refuses overwrite",()=>{var item=ws.ListRecovery(root).First();Reject(()=>ws.Preview(new("restore",item.Path,Path.Combine(root,"Restored.txt"))));});
Test("workspace search finds nested files",()=>{File.WriteAllText(Path.Combine(root,"nested","FindMe.cs"),"class FindMe {}");Assert(ws.Search(root,"FindMe").Count==1);});
Test("workspace read receipt contains screened text",()=>{var p=Path.Combine(root,"New.py");Assert(ws.Apply(ws.Preview(new("read",p)),true).Text=="print(7)");});
Test("workspace list receipt stays inside roots",()=>Assert(!ws.Apply(ws.Preview(new("list",root)),true).Text!.Contains(FileWorkspace.RecoveryFolder)));
Test("workspace binary copies do not become text context",()=>{var p=Path.Combine(root,"Image.png");File.WriteAllBytes(p,[0,1,2,255]);var dest=Path.Combine(root,"ImageCopy.png");ws.Apply(ws.Preview(new("copy",p,dest)),true);Assert(File.ReadAllBytes(dest).SequenceEqual(new byte[]{0,1,2,255}));Reject(()=>ws.Read(dest));});
Test("workspace unavailable other root does not block local root",()=>Assert(new FileWorkspace([root,Path.Combine(parent,"missing-"+Guid.NewGuid())]).List(root).Count>0));
Test("workspace protocol parses unicode writes",()=>{var text=JsonSerializer.Serialize(new[]{new FileAction("write",Path.Combine(root,"Unicode.txt"),Content:"héllo 世界")},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});var parsed=FileActionProtocol.Parse(text);Assert(parsed.Single().Content=="héllo 世界"&&FileActionProtocol.IsActionBlock("json",text));Assert(!FileActionProtocol.IsActionBlock("json","[{\"example\":1}]")&&!FileActionProtocol.IsActionBlock("json","[{\"operation\":\"shell\",\"path\":\"x\"}]"));});
Test("workspace protocol rejects commands and duplicate keys",()=>{Reject(()=>FileActionProtocol.Parse("[{\"operation\":\"read\",\"path\":\"a\",\"command\":\"whoami\"}]"));Reject(()=>FileActionProtocol.Parse("[{\"operation\":\"read\",\"path\":\"a\",\"path\":\"b\"}]"));});
Test("workspace protocol rejects oversized batches",()=>Reject(()=>FileActionProtocol.Parse(JsonSerializer.Serialize(Enumerable.Repeat(new FileAction("list",root),13)))));
Test("workspace protocol cannot authorize roots",()=>Reject(()=>FileActionProtocol.Parse("""[{"operation":"list","path":"C:\\","roots":[]}]""")));
Test("workspace unknown operations cannot execute",()=>Reject(()=>ws.Preview(new("shell",root))));
Test("chat tools execute local read write read with receipts",()=>{
    var target=Path.Combine(root,"Chat.txt");var reviews=0;
    FileActionReceipt? Review(FileAction action){reviews++;return ws.Apply(ws.Preview(action),true);}
    var packet="```infinity-files\n"+JsonSerializer.Serialize(new[]{new{operation="write",path=target,content="hello from the local tool"}})+"\n```";
    var parsed=ChatFileTools.ParseResponse(packet)!;
    var written=ChatFileTools.Execute(parsed,ws,(action,_)=>Task.FromResult(Review(action)),default).GetAwaiter().GetResult();
    var read=ChatFileTools.Execute([new("read",target),new("list",root)],ws,(_,_)=>throw new IOException("Reads must not prompt"),default).GetAwaiter().GetResult();
    Assert(reviews==1&&written.Contains("Executed")&&File.ReadAllText(target)=="hello from the local tool"&&read.Contains("hello from the local tool")&&read.Contains("Chat.txt"));
});
Test("chat tools decline preserves original and reports no execution",()=>{
    var target=Path.Combine(root,"Chat.txt");
    var result=ChatFileTools.Execute([new("write",target,Content:"declined")],ws,(_,_)=>Task.FromResult<FileActionReceipt?>(null),default).GetAwaiter().GetResult();
    Assert(File.ReadAllText(target)=="hello from the local tool"&&result.Contains("Declined; nothing applied")&&!result.Contains("Executed"));
});
Test("chat tools refuse escapes and secrets without invoking review",()=>{
    var result=ChatFileTools.Execute([new("read",Path.Combine(root,"..","outside.txt")),new("read",Path.Combine(root,"Sensitive.txt"))],ws,(_,_)=>throw new IOException("Unexpected review"),default).GetAwaiter().GetResult();
    Assert(result.Contains("Failed;")&&!result.Contains("synthetic-value"));
});
Test("chat tools ignore explanatory and incomplete code examples",()=>{
    var packet="```infinity-files\n[{\"operation\":\"list\",\"path\":\"x\"}]\n```";
    Assert(ChatFileTools.ParseResponse("Example:\n"+packet)==null&&ChatFileTools.ParseResponse(packet+"\nExplanation")==null&&ChatFileTools.ParseResponse(packet[..^3])==null);
    Assert(ChatFileTools.ParseResponse(packet.Replace("infinity-files","json"))?.Single().Operation=="list"&&ChatFileTools.ParseResponse("```json\n[{\"example\":1}]\n```")==null);
    Reject(()=>ChatFileTools.ParseResponse("```infinity-files\n[{\"operation\":\"shell\",\"path\":\"x\"}]\n```"));
});
Test("chat tools cancellation executes nothing",()=>{
    using var stop=new CancellationTokenSource();stop.Cancel();var called=false;
    try{ChatFileTools.Execute([new("write",Path.Combine(root,"Never.txt"),Content:"bad")],ws,(_,_)=>{called=true;return Task.FromResult<FileActionReceipt?>(null);},stop.Token).GetAwaiter().GetResult();throw new IOException("Cancellation ignored");}catch(OperationCanceledException){}
    Assert(!called&&!File.Exists(Path.Combine(root,"Never.txt")));
});
Test("chat tool handoff keeps automatic folder scope and direct execution prohibition",()=>{
    var messages=new Message[]{new("system",FileActionProtocol.Instructions([root])+ChatFileTools.Instructions),new("user","Update the connected file")};
    var handoff=CliEscalation.Prepare(messages,"test");
    Assert(ChatFileTools.Enabled(handoff.Messages)&&handoff.Messages[0].Content.Contains("executes screened reads/writes/mkdir locally")&&handoff.Messages[0].Content.Contains("cannot execute tools or modify files directly"));
    Assert(!ChatFileTools.Enabled([new("user",ChatFileTools.Instructions)]));
});
Test("chat file instructions explain actual read access without contradictory manual-only rules",()=>{
    var instructions=ChatFileTools.WorkspaceInstructions([root]);
    Assert(instructions.Contains("You CAN read actual file contents")&&instructions.Contains("operation")&&instructions.Contains("```infinity-files")&&!instructions.Contains("no automatic execution")&&!instructions.Contains("explicitly included by the user"));
});
Test("file capability answers use connected scope and do not swallow specific file tasks",()=>{
    Assert(ChatFileTools.IsReadCapabilityQuestion("Can you read the files now?")&&ChatFileTools.IsReadCapabilityQuestion("Could you access my project files?")&&!ChatFileTools.IsReadCapabilityQuestion("Can you read Program.cs and explain it?")&&!ChatFileTools.IsReadCapabilityQuestion("Read the files now"));
    Assert(ChatFileTools.ReadCapabilityAnswer([root]).StartsWith("Yes.")&&ChatFileTools.ReadCapabilityAnswer([root]).Contains(root)&&ChatFileTools.ReadCapabilityAnswer([]).Contains("once a folder is connected"));
});
Test("commentary around read-only file calls does not grant write or multi-block execution",()=>{
    var read=JsonSerializer.Serialize(new[]{new{operation="read",path=Path.Combine(root,"Chat.txt")}});var packet="```infinity-files\n"+read+"\n```";
    Assert(ChatFileTools.ParseResponse("I will read that file now.\n"+packet)?.Single().Operation=="read");
    var write=JsonSerializer.Serialize(new[]{new{operation="write",path=Path.Combine(root,"NoProseWrite.txt"),content="not applied"}});
    Assert(ChatFileTools.ParseResponse("I will write.\n```infinity-files\n"+write+"\n```")==null&&ChatFileTools.ParseResponse("I will read.\n"+packet+"\n"+packet)==null);
    Assert(ChatFileTools.ParseResponse("I will write.\n```infinity-files\n"+write+"\n```",true)?.Single().Operation=="write");
    Assert(ChatFileTools.ParseResponse("Example:\n```infinity-files\n"+write+"\n```",true)==null);
});
Test("chat tools accept the reported XML-wrapped read request with prose",()=>{
    var body=JsonSerializer.Serialize(new[]{new{operation="list",path=root}});
    var actions=ChatFileTools.ParseResponse("Let me list the key files.\n\n<infinity-files>\n"+body+"\n</infinity-files>");
    Assert(actions?.Single().Operation=="list");
    var result=ChatFileTools.Execute(actions!,ws,(_,_)=>throw new IOException("Unexpected write"),default).GetAwaiter().GetResult();Assert(result.Contains("Executed")&&result.Contains("Chat.txt"));
});
Test("XML compatibility cannot execute mutations or quoted code examples",()=>{
    var write=JsonSerializer.Serialize(new[]{new{operation="write",path=Path.Combine(root,"Xml.txt"),content="do not execute"}});
    Assert(ChatFileTools.ParseResponse("<infinity-files>"+write+"</infinity-files>")==null&&!File.Exists(Path.Combine(root,"Xml.txt")));
    Assert(ChatFileTools.ParseResponse("```xml\n<infinity-files>[]</infinity-files>\n```")==null);
    Reject(()=>ChatFileTools.ParseResponse("<infinity-files>[{\"operation\":\"shell\",\"path\":\"x\"}]</infinity-files>"));
});
Test("large file pages remove whole-file 128 KiB read obstacle without losing continuation",()=>{
    var path=Path.Combine(root,"LargeLedger.md");var content="BEGIN_MARKER\n"+new string('x',200000)+"\nLATEST_UNFINISHED_MARKER";File.WriteAllText(path,content);
    var first=ws.ReadPage(new("read",path));Assert(first.Offset==0&&first.Text!.StartsWith("BEGIN_MARKER")&&first.Text.Length==6000&&first.HasMore==true&&first.NextOffset==6000&&first.TotalCharacters==content.Length);
    var next=ws.ReadPage(new("read",path,Offset:first.NextOffset,Length:1000));Assert(next.Offset==6000&&next.NextOffset==7000);
    var tail=ws.ReadPage(new("read",path,Offset:-1000));Assert(tail.Text!.EndsWith("LATEST_UNFINISHED_MARKER")&&tail.HasMore==false);
    var parsed=FileActionProtocol.Parse(JsonSerializer.Serialize(new[]{new{operation="read",path,offset=-1000,length=1000}})).Single();Assert(ws.Apply(ws.Preview(parsed),true).Text==tail.Text);
});
Test("paged reads screen the entire file and keep workspace protections",()=>{
    var path=Path.Combine(root,"SensitiveTail.txt");File.WriteAllText(path,new string('a',150000)+"\napi_key = fixture-private-value");Reject(()=>ws.ReadPage(new("read",path,Length:100)));
    Reject(()=>ws.ReadPage(new("read",Path.Combine(root,"appsettings.json"))));Reject(()=>ws.ReadPage(new("read",Path.Combine(root,"..","outside.txt"))));
    Reject(()=>FileActionProtocol.Parse("[{\"operation\":\"write\",\"path\":\"x\",\"offset\":1,\"content\":\"x\"}]"));Reject(()=>ws.ReadPage(new("read",a,Length:0)));
});
Test("paged reads preserve unicode boundaries and end of file",()=>{
    var path=Path.Combine(root,"PagedUnicode.txt");var content="a😀b";File.WriteAllText(path,content);var page=ws.ReadPage(new("read",path,Length:2));Assert(page.Text=="a😀"&&page.NextOffset==3);Assert(ws.ReadPage(new("read",path,Offset:3)).Text=="b");Assert(ws.ReadPage(new("read",path,Offset:4)).Text=="");Reject(()=>ws.ReadPage(new("read",path,Offset:5)));
});
Test("working context releases only older payloads and leaves saved messages intact",()=>{
    string Result(string text)=>ChatContext.ResultPrefix+JsonSerializer.Serialize(new[]{new{status="Executed",Operation="read",Path=a,receipt=new FileActionReceipt("read",a,null,text,Offset:0,NextOffset:text.Length,HasMore:true)}});
    var old=new Message("user",Result(new string('q',20000)));var current=new Message("user",Result("CURRENT_READ_MARKER"));List<Message> saved=[new("system","KEEP_RULES"),new("user","KEEP_USER_REQUEST"),old,new("assistant","prior finding"),current];
    var fit=ChatContext.Fit(saved,4000,(m,_)=>Task.FromResult(m.Sum(x=>x.Content.Length)),default).GetAwaiter().GetResult();Assert(fit.Sum(x=>x.Content.Length)<=4000&&fit[0]==saved[0]&&fit[1]==saved[1]&&fit[^1]==current&&fit[2].Content.Contains("workingContextNotice")&&fit[2].Content.Contains("NextOffset")&&old.Content.Contains(new string('q',20000)));
    var malformed=new[]{new Message("user",ChatContext.ResultPrefix+"invalid"),current};Assert(ChatContext.Fit(malformed,1,(m,_)=>Task.FromResult(m.Sum(x=>x.Content.Length)),default).GetAwaiter().GetResult()[0]==malformed[0]);
});
Test("chat command tool accepts only standalone strict reviewed requests",()=>{
    var packet="```infinity-command\n"+JsonSerializer.Serialize(new{script="dotnet build",workingDirectory=root,timeoutSeconds=120})+"\n```";
    Assert(ChatCommands.ParseResponse(packet)?.Script=="dotnet build"&&ChatCommands.ParseResponse(packet+"\n```")?.Script=="dotnet build"&&ChatCommands.ParseResponse("Here is an example:\n"+packet)==null&&ChatCommands.ParseResponse(packet+"\n"+packet)==null);
    Reject(()=>ChatCommands.ParseResponse("```infinity-command\n{\"script\":\"x\",\"workingDirectory\":\"x\",\"autoRun\":true}\n```"));
});
Test("command context keeps exit evidence and current task while bounding output",()=>{
    var output="FAILURE_BEGIN"+new string('z',30000)+"FAILURE_END";var run=new CommandRun(Guid.NewGuid(),new("dotnet test",root),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"Failed",1,output,"saved-run.json");
    var saved=new Message("user",ChatCommands.ResultPrefix+JsonSerializer.Serialize(run));Message[] input=[new("system","RULES"),new("user","FIX_THIS"),saved];
    var fitted=ChatContext.Fit(input,10000,(m,_)=>Task.FromResult(m.Sum(x=>x.Content.Length)),default).GetAwaiter().GetResult();Assert(fitted[0]==input[0]&&fitted[1]==input[1]&&fitted[^1].Content.Contains("FAILURE_BEGIN")&&fitted[^1].Content.Contains("FAILURE_END")&&fitted[^1].Content.Contains("saved-run.json")&&fitted[^1].Content.Contains("omitted")&&saved.Content.Contains(new string('z',30000)));
});
Test("hybrid helper reuses only a verified identical active installed copy",()=>{
    var installation=Path.Combine(root,"install-fixture");var revision=new string('a',40);var native=Path.Combine(installation,"releases",revision,"Native");Directory.CreateDirectory(native);var helper=Path.Combine(native,"Infinity.RyzenHost.exe");File.WriteAllText(helper,"fixture helper");var bundled=Path.Combine(root,"Bundled.exe");File.Copy(helper,bundled);File.WriteAllText(Path.Combine(installation,"Start.cmd"),"start releases\\"+revision+"\\LocalAI.Desktop.exe");
    var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(helper)));File.WriteAllText(Path.Combine(native,"backend-build.json"),JsonSerializer.Serialize(new{sourceRevision=revision,patch="sequence-growth-v2",sha256=hash}));Assert(HybridBackend.ResolveHost(bundled,installation)==helper);
    File.WriteAllText(bundled,"newer helper");Assert(HybridBackend.ResolveHost(bundled,installation)==bundled);File.Delete(bundled);File.AppendAllText(helper,"tamper");Assert(HybridBackend.ResolveHost(bundled,installation)==bundled);
});
Test("workspace roots persist with conversations",()=>{var dir=Path.Combine(root,"chat-store");var store=new ConversationStore(dir);store.Save(new Conversation{WorkspaceRoots=[root]});Assert(store.Load(out _).Single().WorkspaceRoots.Single()==root);});
Test("workspace drive root resolves descendants",()=>{var drive=Path.GetPathRoot(root)!;Assert(new FileWorkspace([drive]).Resolve(root)==root);});
if(!network&&OperatingSystem.IsWindows())
{
    Test("workspace rejects hard linked overwrite",()=>{var original=Path.Combine(root,"HardOriginal.txt");var link=Path.Combine(root,"HardLink.txt");File.WriteAllText(original,"keep");if(!Native.CreateHardLink(link,original,IntPtr.Zero))throw new IOException("Cannot create controlled hard-link fixture");Reject(()=>ws.Preview(new("write",link,Content:"bad")));Assert(File.ReadAllText(original)=="keep");});
    Test("workspace rejects directory junctions",()=>{var link=Path.Combine(root,"junction");var info=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};info.ArgumentList.Add("/d");info.ArgumentList.Add("/c");info.WorkingDirectory=root;info.ArgumentList.Add("mklink /J junction nested");using var p=System.Diagnostics.Process.Start(info)!;p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Cannot create controlled junction fixture");Reject(()=>ws.Resolve(Path.Combine(link,"FindMe.cs")));});
}
if(network)
{
    var local=Path.Combine(Path.GetTempPath(),"Infinity-cross-network-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(local);var bridge=new FileWorkspace([local,root]);
    Test("workspace copies local files to network",()=>{var source=Path.Combine(local,"Transfer.txt");File.WriteAllText(source,"cross-drive payload");var target=Path.Combine(root,"Transfer.txt");bridge.Apply(bridge.Preview(new("copy",source,target)),true);Assert(File.ReadAllText(target)=="cross-drive payload");});
    Test("workspace moves network files to local with recovery",()=>{var source=Path.Combine(root,"Transfer.txt");var target=Path.Combine(local,"Returned.txt");var receipt=bridge.Apply(bridge.Preview(new("move",source,target)),true);Assert(File.ReadAllText(target)=="cross-drive payload"&&!File.Exists(source)&&receipt.RecoveryPath!=null&&File.ReadAllText(receipt.RecoveryPath)=="cross-drive payload");});
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows));return rows.Any(r=>r.GetType().GetProperty("passed")!.GetValue(r) is false)?1:0;
static class Native
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateHardLink(string file,string existing,IntPtr security);
}
