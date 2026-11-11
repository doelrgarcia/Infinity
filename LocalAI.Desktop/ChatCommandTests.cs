using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    internal async Task ExerciseCodingLoop(Dictionary<string,object> rows,string root,ModelProfile profile,List<string>? packets,bool live)
    {
        conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=false;model.SelectedItem=profile;maximum.Text="2048";contextBudget.Text=profile.ContextLimit.ToString();
        var build="dotnet build './Fixture.csproj' -c Release --nologo";
        var check="$value = & dotnet run --project './Fixture.csproj' -c Release --no-build; if ($LASTEXITCODE -ne 0 -or ($value -join '').Trim() -ne '5') { throw 'Expected 5' }; Write-Output 'ASSERTION_PASSED'";
        var reviews=0;var delayedReviews=0;
        chatCommandReviewTest=async window=>
        {
            try
            {
                // This isolated test approves only its two exact fixture commands.
                if(window.TestRequest.WorkingDirectory!=root||window.TestRequest.Script!=build&&window.TestRequest.Script!=check){rows["unexpectedCommand"]=window.TestRequest.Script;window.Close();return;}
                if(!live){await Task.Delay(1100);delayedReviews++;}
                reviews++;await window.TestExecuteWithCurrentDraft();if(window.Result==null){rows["reviewError"]="Command review did not produce a result";window.Close();}
            }
            catch(Exception e){rows["reviewError"]=e.Message;window.Close();}
        };
        var request=live?$"Repair and verify this isolated C# project. First read {Path.Combine(root,"Program.cs").Replace('\\','/')} with the file tool. Then request exactly this build command: {build}. Use the infinity-command tool with workingDirectory {JsonSerializer.Serialize(root)} and timeoutSeconds 120. Inspect the build result, repair the confirmed compiler error using infinity-files (write valid single-line C# to avoid newline escaping mistakes), then run the identical build again. Finally request exactly this functional test command: {check}. Do not change Fixture.csproj or use other commands. After real results, give a concise outcome. Return only one tool block per turn; never claim you cannot execute commands through Infinity.":"Repair and verify the isolated project.";
        await ExecuteRequest(profile,request,BuildMessages(request),true,false);
        var codingStatus=status.Text;
        var runs=conversation.Messages.Where(m=>m.Content.StartsWith(ChatCommands.ResultPrefix)).Select(m=>JsonSerializer.Deserialize<CommandRun>(m.Content[ChatCommands.ResultPrefix.Length..])).Where(r=>r!=null).ToArray();
        rows["reviewedBuildFailureReturnedToModel"]=runs.Any(r=>r!.Request.Script==build&&r.ExitCode!=0&&r.Output.Contains("CS1002"));
        rows["modelRepairsActualSource"]=File.ReadAllText(Path.Combine(root,"Program.cs"))!="Console.WriteLine(Add(2,3))\nstatic int Add(int a,int b)=>a+b;\n";
        rows["rebuildReallySucceeds"]=runs.Any(r=>r!.Request.Script==build&&r.Status=="Succeeded"&&r.ExitCode==0);
        rows["functionalCheckReallyPasses"]=runs.Any(r=>r!.Request.Script==check&&r.Status=="Succeeded"&&r.ExitCode==0&&r.Output.Contains("ASSERTION_PASSED"));
        rows["eachExecutedCommandWasReviewed"]=reviews==runs.Length&&reviews>=3;
        rows["buildCommandsCleanTheirOwnedTemporaryWork"]=runs.Length>=3&&runs.All(r=>r!.LogError==null);
        if(!live)rows["reviewAndCommandTimeDoNotConsumeModelGenerationBudget"]=delayedReviews>=3&&runs.Any(r=>r!.Request.Script==check&&r.Status=="Succeeded");
        rows["commandResultsAndFinalAnswerPersist"]=store.Load(out _).Single(c=>c.Id==conversation.Id).Messages.Count(m=>m.Content.StartsWith(ChatCommands.ResultPrefix))==runs.Length&&conversation.ActivityState=="Response ready";
        rows["codingWorkDetailsCollapsed"]=transcript.Children.OfType<System.Windows.Controls.Expander>().Any(e=>Equals(e.Tag,"FileActivityDetails")&&!e.IsExpanded);
        if(packets!=null)rows["outgoingPromptExplainsExecutionAndIncludesFailureEvidence"]=packets[0].Contains("You CAN build projects")&&packets.Skip(2).Any(p=>p.Contains("CS1002"));
        rows["noCloudProviderUsed"]=conversation.Messages.Where(m=>m.Provider!=null).All(m=>m.Provider==profile.Id);
        chatCommandReviewTest=w=>w.Close();
        var declined=await RunChatCommand(new("Write-Output 'must not run'",root),default);rows["declineDoesNotExecute"]=declined.Contains("Declined; not executed");
        conversation.ResponseStyle="Plan";var plan=await RunChatCommand(new(build,root),default);rows["planModeNeverRunsCommands"]=plan.Contains("Plan mode");conversation.ResponseStyle="Build";
        var outside=await RunChatCommand(new(build,Path.GetDirectoryName(root)!),default);rows["commandStartingFolderMustBeConnected"]=outside.Contains("outside the connected");
        var previous=conversation;var tiny=new ModelProfile{Id="llama32-hybrid",Label="Small-context fixture",Engine="ryzen",ContextLimit=2048,Endpoint="http://127.0.0.1:1",Executable="missing.exe",ModelPath="missing"};settings.Models.Add(tiny);model.Items.Refresh();NewConversation();model.SelectedItem=tiny;maximum.Text="32";contextBudget.Text="2048";
        var shortQuestion=BuildMessages("What is 2 plus 3?");using(var counter=new LocalClient())rows["smallHybridPlainChatFitsWithoutDroppingRequiredProjectRules"]=await counter.CountTokens(tiny,shortQuestion,default)+32<=2048;
        conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=true;await ExecuteRequest(tiny,"Inspect the project",BuildMessages("Inspect the project"),true,true);
        rows["smallHybridProjectCapacityFailureDoesNotLoadRuntimeOrOpenCloud"]=conversation.Messages.Count==0&&status.Text.StartsWith("Choose Qwen3-Coder");conversation=previous;model.SelectedItem=profile;Render();
        if(rows.Values.Any(v=>v is false))rows["failureDetail"]=codingStatus;
    }
}

internal static class ChatCommandTests
{
    internal static async Task Run(string output,bool live=false)
    {
        var rows=new Dictionary<string,object>();MainWindow? window=null;TcpListener? listener=null;Task? server=null;var packets=new List<string>();using var stop=new CancellationTokenSource();
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"coding-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"Fixture.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root,"Program.cs"),"Console.WriteLine(Add(2,3))\nstatic int Add(int a,int b)=>a+b;\n");
        try
        {
            Settings settings;ModelProfile profile;
            if(live){settings=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));profile=settings.Models.Single(m=>m.Id=="qwen3-coder");settings.Models=[profile];settings.GenerationTimeoutSeconds=240;}
            else
            {
                listener=new(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
                profile=new(){Id="coding-fixture",Engine="ryzen",Endpoint=$"http://127.0.0.1:{port}/",ModelPath=root,ContextLimit=100000};settings=new(){Models=[profile],GenerationTimeoutSeconds=1};
                string Files(params FileAction[] actions)=>"```infinity-files\n"+JsonSerializer.Serialize(actions,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})+"\n```";
                string Command(string script)=>"```infinity-command\n"+JsonSerializer.Serialize(new{script,workingDirectory=root,timeoutSeconds=120})+"\n```";
                var build="dotnet build './Fixture.csproj' -c Release --nologo";var check="$value = & dotnet run --project './Fixture.csproj' -c Release --no-build; if ($LASTEXITCODE -ne 0 -or ($value -join '').Trim() -ne '5') { throw 'Expected 5' }; Write-Output 'ASSERTION_PASSED'";
                string[] replies=[Files(new FileAction("read",Path.Combine(root,"Program.cs"))),Command(build),Files(new FileAction("write",Path.Combine(root,"Program.cs"),Content:"Console.WriteLine(Add(2,3));\nstatic int Add(int a,int b)=>a+b;\n")),Command(build),Command(check),"Fixed compiler error; build and functional check passed."];
                server=Task.Run(async()=>
                {
                    try{while(true)
                    {
                        using var connection=await listener.AcceptTcpClientAsync(stop.Token);using var stream=connection.GetStream();
                        var one=new byte[1];var header=new List<byte>();
                        while(header.Count<32768){await stream.ReadExactlyAsync(one,stop.Token);header.Add(one[0]);if(header.Count>=4&&Encoding.ASCII.GetString(header.TakeLast(4).ToArray())=="\r\n\r\n")break;}
                        var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var sizeLine=lines.FirstOrDefault(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));
                        var body=new byte[sizeLine==null?0:int.Parse(sizeLine.Split(':')[1])];await stream.ReadExactlyAsync(body,stop.Token);
                        if(lines.Any(l=>l.Equals("Transfer-Encoding: chunked",StringComparison.OrdinalIgnoreCase)))
                        {
                            async Task<string> Line(){var buffer=new List<byte>();while(true){await stream.ReadExactlyAsync(one,stop.Token);buffer.Add(one[0]);if(buffer.Count>=2&&buffer[^2]==13&&buffer[^1]==10)return Encoding.ASCII.GetString(buffer.ToArray()[..^2]);}}
                            using var decoded=new MemoryStream();while(true){var size=Convert.ToInt32((await Line()).Split(';')[0],16);if(size==0){await Line();break;}if(size>2_000_000||decoded.Length+size>2_000_000)throw new IOException("Fixture body too large");var chunk=new byte[size];await stream.ReadExactlyAsync(chunk,stop.Token);decoded.Write(chunk);await Line();}body=decoded.ToArray();
                        }
                        string response;if(lines[0].Contains("/health"))response=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});
                        else{packets.Add(Encoding.UTF8.GetString(body));var reply=replies[Math.Min(packets.Count-1,replies.Length-1)];response="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=reply},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}
                        var bytes=Encoding.UTF8.GetBytes(response);await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nContent-Type: application/json\r\n\r\n"),stop.Token);await stream.WriteAsync(bytes,stop.Token);
                    }}catch(OperationCanceledException){}catch(SocketException){}catch(ObjectDisposedException){}
                });
            }
            window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"coding-window.json")],settings);
            window.Show();await window.ExerciseCodingLoop(rows,root,profile,live?null:packets,live);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();stop.Cancel();listener?.Stop();if(server!=null)await server;}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));var exit=rows.Count>0&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
}
