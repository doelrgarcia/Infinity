using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LocalAI.Core;
public sealed class CliTranscript
{
    public string Text {get;private set;}="";
    public string? Finish {get;private set;}
    public string AppendEvent(string line,string engine)
    {
        using var doc=JsonDocument.Parse(line);var root=doc.RootElement;string? addition=null;bool snapshot=false;
        var type=root.TryGetProperty("type",out var t)?t.GetString():null;
        if(engine=="copilot")
        {
            if(type is "session.error" or "error")throw new IOException(CliAssistant.Failure(root.TryGetProperty("data",out var error)?error.ToString():root.ToString(),1));
            if(type is "tool.execution_start" or "tool.execution_complete")throw new IOException("Copilot tried to run its own tools. Use Infinity's reviewed file/command tools.");
            if(Finish!=null)throw new IOException("Copilot sent output after completion.");
            if(type=="result")
            {if(!root.TryGetProperty("exitCode",out var exit)||exit.GetInt32()!=0)throw new IOException("Copilot could not complete this request. Check its sign-in and account access.");Finish="stop";return "";}
            if(type=="assistant.message_delta")addition=root.GetProperty("data").GetProperty("deltaContent").GetString();
            else if(type=="assistant.message")
            {
                var data=root.GetProperty("data");
                if(data.TryGetProperty("toolRequests",out var tools)&&tools.ValueKind==JsonValueKind.Array&&tools.GetArrayLength()>0)throw new IOException("Copilot requested native tools. Use Infinity's reviewed tools.");
                addition=data.GetProperty("content").GetString();snapshot=true;
            }
            else return "";
        }
        if(engine=="deepseek"){if(type=="error")throw new IOException(DeepSeekFailure(root.TryGetProperty("code",out var code)?code.GetString():null));if(Finish!=null)throw new IOException("DeepSeek sent output after completion.");if(type=="text")addition=root.GetProperty("text").GetString();else if(type=="final"){Finish=root.GetProperty("finish").GetString();if(Finish is not ("stop" or "max-tokens"))throw new IOException("Invalid DeepSeek completion.");return "";}else throw new IOException("Unexpected DeepSeek bridge event.");}
        if(type=="error"||root.TryGetProperty("is_error",out var failed)&&failed.ValueKind==JsonValueKind.True)throw new IOException(CliAssistant.Failure(root.ToString(),1));
        if(type=="tool_call"||engine=="codex"&&root.TryGetProperty("item",out var toolItem)&&toolItem.TryGetProperty("type",out var toolType)&&toolType.GetString() is "command_execution" or "file_change" or "mcp_tool_call")throw new IOException("Provider tried to run its own tools. Use Infinity's reviewed file/command tools for this request.");
        if(type=="stream_event"&&root.TryGetProperty("event",out var toolEvent)&&toolEvent.TryGetProperty("content_block",out var toolBlock)&&toolBlock.TryGetProperty("type",out var blockType)&&blockType.GetString()=="tool_use")throw new IOException("Provider tried to run its own tools. Use Infinity's reviewed file/command tools for this request.");
        if(engine=="codex"&&type=="item.completed"&&root.TryGetProperty("item",out var item)&&item.TryGetProperty("type",out var it)&&it.GetString()=="agent_message")addition=item.GetProperty("text").GetString();
        else if(type=="stream_event"&&root.TryGetProperty("event",out var ev)&&ev.TryGetProperty("delta",out var delta)&&delta.TryGetProperty("text",out var d))addition=d.GetString();
        else if(type=="assistant"&&root.TryGetProperty("message",out var message)&&message.TryGetProperty("content",out var content)&&content.ValueKind==JsonValueKind.Array)
        {addition=string.Concat(content.EnumerateArray().Where(c=>c.TryGetProperty("type",out var ct)&&ct.GetString()=="text").Select(c=>c.GetProperty("text").GetString()));snapshot=true;}
        else if(type=="result"&&root.TryGetProperty("result",out var result)&&result.ValueKind==JsonValueKind.String){addition=result.GetString();snapshot=true;}
        if(string.IsNullOrEmpty(addition))return "";
        if(snapshot){if(addition.StartsWith(Text,StringComparison.Ordinal))addition=addition[Text.Length..];else if(Text.Length>0)return "";}
        Text+=addition;if(Text.Length>1_000_000)throw new IOException("Provider response exceeds 1 million characters.");return addition;
    }
    private static string DeepSeekFailure(string? code)=>code switch{"ACCOUNT_SIGN_IN_REQUIRED" or "ACCOUNT_TOKEN_INVALID"=>"Sign in through the installed DeepSeek Harness app, then retry in Infinity. No API key is required.","ACCOUNT_QUOTA_EXCEEDED" or "QUOTA_EXCEEDED"=>"DeepSeek account usage limit reached. Check your Harness account; no purchase was made.",_=>"DeepSeek could not finish this request. Check Harness sign-in or connectivity, then retry."};
}
public sealed class CliAssistant
{
    // Cursor accepts positional prompts. Assemble argv inside its Node process so private
    // prompts never enter the Windows command line or its 32K quoting/length limit.
    public const int MaximumPromptCharacters=131072;
    public const int MaximumPromptUtf8Bytes=MaximumPromptCharacters*4;
    public static readonly string CursorStdinBootstrap="(async()=>{const target=process.argv[1],args=process.argv.slice(2),parts=[];let size=0;for await(const part of process.stdin){size+=part.length;if(size>"+MaximumPromptUtf8Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)+")throw Error('Prompt input too large');parts.push(part)}const prompt=new TextDecoder('utf-8',{fatal:true}).decode(Buffer.concat(parts));process.argv=[process.execPath,target,...args,prompt];require(target)})().catch(()=>{process.stderr.write('Infinity Cursor prompt input failed.\\n');process.exitCode=1})";
    public static string FormatPrompt(IReadOnlyList<Message> messages)=>string.Join("\n\n",messages.Select(m=>$"{m.Role.ToUpperInvariant()}:\n{FileAttachment.InputText(m)}"+(m.Images==null?"":"\n"+string.Join("\n",m.Images.Select(i=>$"ATTACHED IMAGE: {i.Name} | {i.Mime} | SHA256 {i.Sha256}")))));
    public static string Failure(string diagnostic,int exitCode)
    {
        // Structured events contain usage/modelUsage accounting even for unrelated errors.
        // Classify only error details, never those metadata property names.
        try
        {
            using var json=JsonDocument.Parse(diagnostic);
            var details=new List<string>();
            void Collect(JsonElement value,int depth)
            {
                if(depth>4)return;
                if(value.ValueKind==JsonValueKind.String){details.Add(value.GetString()!);return;}
                if(value.ValueKind==JsonValueKind.Array){foreach(var item in value.EnumerateArray().Take(32))Collect(item,depth+1);return;}
                if(value.ValueKind==JsonValueKind.Object)foreach(var field in new[]{"errors","error","message","code","result","subtype"})if(value.TryGetProperty(field,out var item))Collect(item,depth+1);
            }
            Collect(json.RootElement,0);diagnostic=string.Join("\n",details);
        }
        catch(JsonException){ } // Plain stderr remains useful; raw details are never returned.
        bool Has(params string[] terms)=>terms.Any(term=>diagnostic.Contains(term,StringComparison.OrdinalIgnoreCase));
        if(Has("not signed in","not logged in","login required","sign-in required","sign in required","unauthorized","authentication","authenticate","invalid api key","missing api key","401"))return "Provider sign-in is required. Sign in through its CLI, then retry. Desktop and CLI sign-ins can be separate.";
        if(Has("workspace trust","trust required","trust this workspace","workspace is not trusted","untrusted workspace"))return "Provider requires workspace trust. Open its CLI for the LocalAI bridge workspace and review the trust request yourself, then retry.";
        if(Has("quota","usage limit","usage_limit","rate limit","rate_limit","too many requests","429"))return "Provider account usage limit reached. Check that provider's account/plan and retry; no purchase or limit change was made.";
        if(Has("error_max_turns","maximum turns reached","max turns reached","max turns limit","turn limit reached"))return "Provider reached its response turn limit. No account quota change was made.";
        return $"Provider CLI exited with code {exitCode} or returned no assistant text. Open its CLI to check account access, version and connectivity.";
    }
    public static ProcessStartInfo CreateStart(ModelProfile profile,string workspace,string promptPath,string prompt,IReadOnlyList<string>? imagePaths=null)
    {
        if(profile.Engine=="deepseek"){if(imagePaths?.Count>0)throw new IOException("DeepSeek attachments are not enabled yet.");return DeepSeekBridge.CreateStart(profile,workspace);}
        if(profile.Engine is not ("codex" or "cursor" or "grok" or "copilot"))throw new InvalidDataException("Unknown CLI provider.");
        if(!File.Exists(profile.Executable))throw new FileNotFoundException("Provider CLI not found. Check the AI apps & CLI panel and appsettings.json.");
        var info=new ProcessStartInfo(profile.Executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workspace,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
        foreach(var key in new[]{"NODE_OPTIONS","NODE_PATH","CODEX_THREAD_ID","CODEX_INTERNAL_ORIGINATOR_OVERRIDE","CURSOR_INVOKED_AS"})info.Environment.Remove(key);
        if(profile.Engine=="copilot")foreach(var key in new[]{"COPILOT_ALLOW_ALL","COPILOT_ASSISTED_APPROVAL","COPILOT_AUTO_APPROVAL","COPILOT_AGENT_MODE"})info.Environment.Remove(key);
        info.Environment["NO_COLOR"]="1";info.Environment["GROK_AGENT_DASHBOARD"]="0";
        if(profile.Engine=="cursor")info.Environment["PATH"]=Path.GetDirectoryName(profile.Executable)+Path.PathSeparator+(info.Environment.TryGetValue("PATH",out var searchPath)?searchPath:"");
        string[] args=profile.Engine switch
        {
            "codex"=>["exec","--ignore-user-config","--sandbox","read-only","--disable","shell_tool","--disable","multi_agent","--skip-git-repo-check","--ephemeral","--json","--color","never","-"],
            "cursor"=>["-e",CursorStdinBootstrap,"--",profile.ModelPath,"--print","--mode","ask","--sandbox",OperatingSystem.IsWindows()?"disabled":"enabled","--output-format","stream-json","--stream-partial-output","--workspace",workspace],
            // An empty available-tools value exposes ALL tools (upstream CLI #5306).
            // A nonmatching name exposes none; explicit deny rules protect file/shell/URL access too.
            "copilot"=>["--available-tools=__infinity_no_native_tools__","--deny-tool","shell","--deny-tool","write","--deny-tool","read","--deny-tool","url","--deny-tool","memory","--disallow-temp-dir","--disable-builtin-mcps","--no-custom-instructions","--no-auto-update","--no-ask-user","--output-format","json","--stream","on","--silent","--log-level","none","--sync-session=off"],
            _=>["--permission-mode","plan","--tools","","--no-subagents","--disable-web-search","--max-turns","1","--output-format","streaming-messages-json","--prompt-file",promptPath]
        };
        if(profile.Engine=="cursor"&&!File.Exists(profile.ModelPath))throw new FileNotFoundException("Cursor CLI script not found. Update its installed version path in appsettings.json.");
        foreach(var arg in args)info.ArgumentList.Add(arg);
        if(imagePaths?.Count>0){if(profile.Engine!="codex")throw new IOException("This provider bridge cannot receive images.");foreach(var path in imagePaths){info.ArgumentList.Insert(info.ArgumentList.Count-1,"--image");info.ArgumentList.Insert(info.ArgumentList.Count-1,path);}}
        // Legacy TrustBridgeWorkspace is retained in configuration but never bypasses Cursor trust.
        return info;
    }
    public async Task<InferenceResult> Generate(ModelProfile profile,IReadOnlyList<Message> messages,Action<string> chunk,CancellationToken token)
    {
        ImageInput.RequireProvider(profile,messages);var images=ImageInput.All(messages);
        var prompt=FormatPrompt(messages);
        if(prompt.Length>MaximumPromptCharacters)throw new IOException($"This request exceeds the {MaximumPromptCharacters:N0}-character provider bridge allowance. Start a new chat or use shorter source excerpts.");
        var directory=Path.Combine(AppPaths.DataRoot,"cli-bridge",profile.Engine);Directory.CreateDirectory(directory);
        if(profile.Engine=="cursor")
        {
            // Windows has no Cursor OS sandbox. The private bridge denies all documented tool classes.
            var policy=Path.Combine(directory,".cursor");Directory.CreateDirectory(policy);
            await File.WriteAllTextAsync(Path.Combine(policy,"cli.json"),"""{"permissions":{"allow":[],"deny":["Shell(*)","Read(*)","Read(**)","Write(*)","Write(**)","WebFetch(*)","Mcp(*:*)"]}}""",token);
            await File.WriteAllTextAsync(Path.Combine(policy,"deny-tools.cjs"),"""process.stdout.write(JSON.stringify({permission:"deny",user_message:"LocalAI text-only bridge blocks tools",agent_message:"All tools are disabled. Answer with text only."}));process.exitCode=2;""",token);
            await File.WriteAllTextAsync(Path.Combine(policy,"hooks.json"),"""{"version":1,"hooks":{"preToolUse":[{"command":"node .cursor/deny-tools.cjs","timeout":5}],"beforeReadFile":[{"command":"node .cursor/deny-tools.cjs","timeout":5}],"beforeShellExecution":[{"command":"node .cursor/deny-tools.cjs","timeout":5}],"beforeMCPExecution":[{"command":"node .cursor/deny-tools.cjs","timeout":5}]}}""",token);
        }
        var promptPath=Path.Combine(directory,"request-"+Guid.NewGuid().ToString("N")+".txt");if(profile.Engine=="grok")await File.WriteAllTextAsync(promptPath,prompt,token);
        var imagePaths=new List<string>();
        var watch=Stopwatch.StartNew();double? first=null;var transcript=new CliTranscript();
        try
        {
            foreach(var image in images){var path=Path.Combine(directory,"image-"+Guid.NewGuid().ToString("N")+(image.Mime=="image/png"?".png":".jpg"));imagePaths.Add(path);await File.WriteAllBytesAsync(path,image.Bytes(),token);}
            using var process=new Process{StartInfo=CreateStart(profile,directory,promptPath,prompt,imagePaths)};
            if(!process.Start())throw new IOException("Provider CLI could not start.");
            try
            {
                using var stopping=CancellationTokenSource.CreateLinkedTokenSource(token);var ioToken=stopping.Token;
                if(profile.Engine=="deepseek")await process.StandardInput.WriteAsync(JsonSerializer.Serialize(messages.Select(m=>new{role=m.Role,content=FileAttachment.InputText(m)})).AsMemory(),token);
                else if(profile.Engine is "codex" or "cursor" or "copilot")await process.StandardInput.WriteAsync(prompt.AsMemory(),token);process.StandardInput.Close();
                var diagnostics=new StringBuilder();
                async Task DrainErrors(){try{var chars=new char[2048];int n;while((n=await process.StandardError.ReadAsync(chars,ioToken))>0){if(diagnostics.Length+n>65536)throw new IOException("Provider diagnostic output limit exceeded.");diagnostics.Append(chars,0,n);}}catch{stopping.Cancel();throw;}}
                var stderr=DrainErrors();long total=0;
                while(await process.StandardOutput.ReadLineAsync(ioToken) is { } line)
                {
                    total+=line.Length;if(total>2_000_000)throw new IOException("Provider event output exceeds 2 million characters.");
                    try{var text=transcript.AppendEvent(line,profile.Engine);if(text.Length>0){first??=watch.Elapsed.TotalSeconds;chunk(text);}}
                    catch(JsonException){if(profile.Engine is "deepseek" or "copilot")throw new IOException("Invalid provider bridge response; request stopped.");if(diagnostics.Length<60000)diagnostics.AppendLine(line[..Math.Min(line.Length,1000)]);}
                }
                await process.WaitForExitAsync(token);await stderr;
                if(process.ExitCode!=0||transcript.Text.Length==0||profile.Engine is ("deepseek" or "copilot")&&transcript.Finish==null)throw new IOException(Failure(diagnostics.ToString(),process.ExitCode));
                return new(transcript.Text,first??watch.Elapsed.TotalSeconds,watch.Elapsed.TotalSeconds,null,null,null,null,transcript.Finish=="max-tokens"?"length":null);
            }
            catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}throw;}
        }
        finally{foreach(var path in imagePaths.Append(promptPath)){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}}
    }
}
