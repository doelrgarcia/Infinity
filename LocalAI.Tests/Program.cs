using System.Net;
using System.Text;
using System.Text.Json;
using LocalAI.Core;

var results=new List<object>();
void Check(string name,Action action){try{action();results.Add(new{name,passed=true});}catch(Exception e){results.Add(new{name,passed=false,error=e.Message});}}
void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or IOException or ArgumentException){return;}throw new Exception("Expected rejection");}
var dir=Path.Combine(Path.GetTempPath(),"LocalAI-tests-"+Guid.NewGuid());Directory.CreateDirectory(dir);
foreach(var bad in new[]{"https://127.0.0.1:8000","http://localhost:8000","http://example.com","http://127.0.0.1.evil.test","http://127.0.0.1:8000/path","http://user:password@127.0.0.1:8000","http://127.0.0.1:8000/?secret=x","http://[::1]:8000"})Check("reject endpoint "+bad.Replace("password","REDACTED"),()=>Reject(()=>LocalClient.ValidateEndpoint(bad)));
Check("loopback accepted",()=>Assert(LocalClient.ValidateEndpoint("http://127.0.0.1:8017").Port==8017));
Check("source read",()=>{var path=Path.Combine(dir,"Safe.cs");File.WriteAllText(path,"public class Safe {}");Assert(ContextFiles.Read(path).Content.Contains("class Safe"));});
Check("source reads and folder discovery honour cancellation",()=>{using var cancelled=new CancellationTokenSource();cancelled.Cancel();var count=0;try{ContextFiles.Read(Path.Combine(dir,"Safe.cs"),cancelled.Token);}catch(OperationCanceledException){count++;}try{ContextFiles.Discover(dir,cancelled.Token).ToArray();}catch(OperationCanceledException){count++;}Assert(count==2);});
Check("configuration excluded",()=>Assert(!ContextFiles.Allowed(Path.Combine(dir,"appsettings.json"))));
Check("generated folders excluded",()=>Assert(!ContextFiles.Allowed(Path.Combine(dir,"obj","Safe.cs"))));
Check("secret detection",()=>{var path=Path.Combine(dir,"Unsafe.cs");File.WriteAllText(path,"var password = \"synthetic-secret-value\";");Reject(()=>ContextFiles.Read(path));});
Check("binary detection",()=>{var path=Path.Combine(dir,"Binary.cs");File.WriteAllText(path,"abc\0def");Reject(()=>ContextFiles.Read(path));});
Check("large file excluded",()=>{var path=Path.Combine(dir,"Large.cs");File.WriteAllText(path,new string('x',131073));Reject(()=>ContextFiles.Read(path));});
Check("context source delimiters",()=>Assert(ContextFiles.Format([new("Safe.cs","class Safe{}")]).Contains("<untrusted_source>")));
Check("conversation roundtrip and overwrite",()=>{var s=new ConversationStore(Path.Combine(dir,"chats"));var c=new Conversation{Messages=[new("user","one")]};s.Save(c);c.Messages.Add(new("assistant","two"));s.Save(c);Assert(s.Load(out _).Single().Messages.Count==2);});
Check("legacy chat receives safe task defaults",()=>{var c=JsonSerializer.Deserialize<Conversation>("{\"Title\":\"Legacy\",\"Messages\":[]}")!;Assert(c.TaskBrief==""&&c.ResponseStyle=="Build");});
Check("task brief style and clearing survive reload",()=>{var s=new ConversationStore(Path.Combine(dir,"task-chats"));var c=new Conversation{TaskBrief="Keep Acme dark",ResponseStyle="Plan"};s.Save(c);var loaded=s.Load(out _).Single();Assert(loaded.TaskBrief==c.TaskBrief&&loaded.ResponseStyle=="Plan");loaded.TaskBrief="";s.Save(loaded);Assert(s.Load(out _).Single().TaskBrief=="");});
Check("null task data and unknown style load safely",()=>{var path=Path.Combine(dir,"legacy-task");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"legacy.json"),"{\"TaskBrief\":null,\"ResponseStyle\":\"unknown\",\"Messages\":[]}");var loaded=new ConversationStore(path).Load(out _).Single();Assert(loaded.TaskBrief==""&&loaded.ResponseStyle=="Build");});
Check("corrupt conversation retained",()=>{var chat=Path.Combine(dir,"broken");Directory.CreateDirectory(chat);File.WriteAllText(Path.Combine(chat,"broken.json"),"{");Assert(new ConversationStore(chat).Load(out var warning).Count==0&&warning!=""&&File.Exists(Path.Combine(chat,"broken.json")));});
Check("invalid saved chat structures skipped without deleting originals",()=>{var path=Path.Combine(dir,"invalid-structure");Directory.CreateDirectory(path);var payloads=new[]{"null","{\"Messages\":null}","{\"Messages\":[null]}","{\"Messages\":[{\"Role\":\"user\",\"Content\":null}]}","{\"Messages\":[{\"Role\":\"system\",\"Content\":\"Unexpected system instruction\"}]}"};for(var i=0;i<payloads.Length;i++)File.WriteAllText(Path.Combine(path,i+".json"),payloads[i]);var loaded=new ConversationStore(path).Load(out var warning);Assert(loaded.Count==0&&warning!=""&&Directory.GetFiles(path,"*.json").Length==payloads.Length);});
Check("null saved chat labels load with safe defaults",()=>{var path=Path.Combine(dir,"null-labels");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"chat.json"),"{\"Title\":null,\"ModelId\":null,\"Messages\":[]}");var loaded=new ConversationStore(path).Load(out _).Single();Assert(loaded.Title=="New conversation"&&loaded.ModelId=="");});
Check("duplicate saved chat IDs cannot collide",()=>{var path=Path.Combine(dir,"duplicate-chats");Directory.CreateDirectory(path);var c=new Conversation{Messages=[new("user","Synthetic duplicate")]};var data=JsonSerializer.Serialize(c);File.WriteAllText(Path.Combine(path,"one.json"),data);File.WriteAllText(Path.Combine(path,"two.json"),data);Assert(new ConversationStore(path).Load(out var warning).Count==1&&warning!=""&&Directory.GetFiles(path).Length==2);});
using(var client=new LocalClient())
{
    async Task Test(string name,Func<Task> action){try{await action();results.Add(new{name,passed=true});}catch(Exception e){results.Add(new{name,passed=false,error=e.Message});}}
    if(!args.Contains("--offline"))
    {
    var model=Settings.Load(Path.Combine(Directory.GetCurrentDirectory(),"appsettings.json")).Models.Single(m=>m.Id=="qwen25-coder");
    await Test("real runtime identity",async()=>Assert(await client.Healthy(model,CancellationToken.None)));
    await Test("7B model returns correct complete answer",async()=>{var text=new StringBuilder();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var result=await client.Generate(model,[new("user","Write only the result of 3 + 4 as a single digit.")],16,0,chunk=>text.Append(chunk),timeout.Token);Assert(result.Text.Trim()=="7"&&text.ToString()==result.Text);});
    await Test("wrong model identity refused",async()=>{model.Id="wrong";try{await client.Healthy(model,CancellationToken.None);throw new Exception("Expected identity refusal");}catch(InvalidDataException){}finally{model.Id="qwen25-coder";}});
    await Test("wrong model path refused",async()=>{var path=model.ModelPath;model.ModelPath=Path.Combine(dir,"different.gguf");try{await client.Healthy(model,CancellationToken.None);throw new Exception("Expected path refusal");}catch(InvalidDataException){}finally{model.ModelPath=path;}});
    await Test("exact model tokenizer",async()=>{var count=await client.CountTokens(model,[new("user","Hello world")],CancellationToken.None);Assert(count>2&&count<100);});
    await Test("real context overflow refused",async()=>{try{await client.Generate(model,[new("user",new string('z',40000))],128,0,_=>{},CancellationToken.None);throw new Exception("Expected overflow refusal");}catch(InvalidOperationException){}});
    await Test("cancel real stream",async()=>{using var cts=new CancellationTokenSource(TimeSpan.FromMilliseconds(500));try{await client.Generate(model,[new("user","Write a long coding tutorial.")],512,0,_=>{},cts.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}});
    }
    await Test("unavailable runtime actionable",async()=>{var p=new ModelProfile{Id="missing",Endpoint="http://127.0.0.1:8029",Executable=Path.Combine(dir,"missing.exe"),ModelPath=Path.Combine(dir,"missing.gguf")};using var manager=new RuntimeManager();try{await manager.Ensure(p,client,CancellationToken.None);throw new Exception("Expected missing runtime");}catch(FileNotFoundException e){Assert(e.Message.Contains("installation"));}});
    // Controlled local fault server validates production SSE parser and transport boundaries.
    using var listener=new HttpListener();listener.Prefixes.Add("http://127.0.0.1:8028/");listener.Start();
    await Test("Agent streamed action uses constrained schema and preserves source escapes",async()=>
    {
        var source="let sample = \"quoted\"\n// backslash \\ and unicode 测试";string? received=null;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serve=Task.Run(async()=>{for(var n=0;n<3;n++){var c=await listener.GetContextAsync().WaitAsync(deadline.Token);using var reader=new StreamReader(c.Request.InputStream);var body=await reader.ReadToEndAsync(deadline.Token);string response;if(c.Request.Url!.AbsolutePath=="/apply-template")response="{\"prompt\":\"fixture\"}";else if(c.Request.Url.AbsolutePath=="/tokenize")response="{\"tokens\":[1]}";else{received=body;var text=JsonSerializer.Serialize(new{action="proposeFile",value="",target="C:/fixture/Safe.swift",content=source});response="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=text},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(response),deadline.Token);c.Response.Close();}});
        var answer=await client.Generate(new(){Id="schema-fixture",Engine="llama",Endpoint="http://127.0.0.1:8028",ContextLimit=2048},[new("user","Draft only")],128,0,_=>{},deadline.Token,true);await serve;
        using var payload=JsonDocument.Parse(received!);var format=payload.RootElement.GetProperty("response_format");var schema=format.GetProperty("schema");var call=AgentProtocol.Parse(answer.Text);
        Assert(format.GetProperty("type").GetString()=="json_object"&&schema.GetProperty("type").GetString()=="object"&&!schema.GetProperty("additionalProperties").GetBoolean()&&schema.GetProperty("properties").GetProperty("action").GetProperty("enum").EnumerateArray().Any(x=>x.GetString()=="proposeFile")&&call.Content==source&&call.Action=="proposeFile");
    });
    var llamaTemplateServer=Task.Run(async()=>{var c=await listener.GetContextAsync();var body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();using var json=JsonDocument.Parse(body);var root=json.RootElement;var valid=c.Request.Url!.AbsolutePath=="/v1/responses"&&root.GetProperty("input").GetString()=="<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\nFollow instructions.<|eot_id|><|start_header_id|>user<|end_header_id|>\n\n3 + 4?<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n"&&root.GetProperty("max_output_tokens").GetInt32()==16&&!root.TryGetProperty("messages",out _);c.Response.StatusCode=valid?200:400;c.Response.ContentType="text/event-stream";await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"7<|eo\"}\n\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"t_id|>\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"finish_reason\":\"stop\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"7<|eot_id|>\"}]}]}}\n\ndata: [DONE]\n\n"));c.Response.Close();});
    await Test("hybrid Llama template and Responses stream",async()=>{var profile=new ModelProfile{Id="llama32-hybrid",Endpoint="http://127.0.0.1:8028",Engine="ryzen",ContextLimit=2048};var answer=await client.Generate(profile,[new("system","Follow instructions."),new("user","3 + 4?")],16,0,_=>{},CancellationToken.None);Assert(answer.Text=="7"&&answer.FinishReason=="stop");});await llamaTemplateServer;
    async Task HybridFault(string name,string body)
    {
        var serve=Task.Run(async()=>{var c=await listener.GetContextAsync();c.Response.ContentType="text/event-stream";await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));c.Response.Close();});
        await Test(name,async()=>{try{await client.Generate(new(){Id="llama32-hybrid",Engine="ryzen",Endpoint="http://127.0.0.1:8028",ContextLimit=2048},[new("user","3+4?")],16,0,_=>{},CancellationToken.None);throw new Exception("Expected hybrid protocol refusal");}catch(Exception e) when(e is InvalidDataException or EndOfStreamException){}});await serve;
    }
    var limitedResponse=Task.Run(async()=>{var c=await listener.GetContextAsync();var delta=JsonSerializer.Serialize(new{type="response.output_text.delta",delta="Partial"});var completed=JsonSerializer.Serialize(new{type="response.completed",response=new{finish_reason="length",output=new[]{new{content=new[]{new{type="output_text",text="Partial"}}}}}});await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: "+delta+"\n\ndata: "+completed+"\n\ndata: [DONE]\n\n"));c.Response.Close();});
    await Test("hybrid output limit remains visible for escalation",async()=>{var limitedHybrid=await client.Generate(new(){Id="llama32-hybrid",Engine="ryzen",Endpoint="http://127.0.0.1:8028",ContextLimit=1024},[new("user","test")],16,0,_=>{},default);Assert(limitedHybrid.Text=="Partial"&&limitedHybrid.FinishReason=="length");});await limitedResponse;
    var hybridDelta=JsonSerializer.Serialize(new{type="response.output_text.delta",delta="7"});var wrongSnapshot=JsonSerializer.Serialize(new{type="response.completed",response=new{finish_reason="stop",output=new[]{new{content=new[]{new{type="output_text",text="8"}}}}}});
    await HybridFault("hybrid stream requires explicit completed event",$"data: {hybridDelta}\n\ndata: [DONE]\n\n");
    await HybridFault("hybrid final snapshot mismatch refused",$"data: {hybridDelta}\n\ndata: {wrongSnapshot}\n\ndata: [DONE]\n\n");
    async Task FaultTest(string name,string body,int status,bool expectSuccess,string? finishReason=null)
    {
        var serve=Task.Run(async()=>{var c=await listener.GetContextAsync();c.Response.StatusCode=status;c.Response.ContentType="text/event-stream";var bytes=Encoding.UTF8.GetBytes(body);await c.Response.OutputStream.WriteAsync(bytes);c.Response.Close();});
        await Test(name,async()=>{var p=new ModelProfile{Id="test",Endpoint="http://127.0.0.1:8028",Engine="ryzen",ContextLimit=2048};try{var result=await client.Generate(p,[new("user","hello")],128,0,_=>{},CancellationToken.None);Assert(expectSuccess&&result.Text=="hello"&&result.FinishReason==finishReason);}catch(Exception e) when(!expectSuccess && e is HttpRequestException or InvalidDataException or JsonException or EndOfStreamException){}});await serve;
    }
    await FaultTest("streamed events and usage","data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\ndata: {\"usage\":{\"completion_tokens\":1,\"decoding_speed_tps\":12.5}}\n\ndata: [DONE]\n\n",200,true);
    await FaultTest("output token limit reported","data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n",200,true,"length");
    await FaultTest("truncated stream refused","data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n",200,false);
    await FaultTest("runtime error refused","data: {\"error\":\"fault\"}\n\n",200,false);
    await FaultTest("redirect refused","",302,false);
    await FaultTest("HTTP error actionable","",503,false);
}
var output=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(output);
if(args.Length>0)File.WriteAllText(args[0],output);
return results.Any(r=>!(bool)r.GetType().GetProperty("passed")!.GetValue(r)!)?1:0;
