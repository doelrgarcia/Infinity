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
    internal string? ChatFileTestLog=>runtime.LastLogPath;
    internal async Task ExerciseLiveChatFileTools(Dictionary<string,object> rows,string root)
    {
        conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=false;
        var marker="LOCAL_FILE_"+Guid.NewGuid().ToString("N");var input=Path.Combine(root,"Input.txt");var output=Path.Combine(root,"Output.txt");File.WriteAllText(input,marker);
        var profile=settings.Models.Single();model.SelectedItem=profile;maximum.Text="2048";contextBudget.Text=profile.ContextLimit.ToString();
        var request=$"Read {input.Replace('\\','/')} using the infinity-files tool. Then write its exact contents, unchanged, into {output.Replace('\\','/')} using the write tool. Do the work now. Return only one fenced infinity-files array per tool turn; after the write receipt, reply briefly. Do not propose commands.";
        await ExecuteRequest(profile,request,BuildMessages(request),true,false);
        rows["realLocalModelReadsActualUnknownFile"]=conversation.Messages.Any(m=>m.Content.StartsWith("File tool results")&&m.Content.Contains(marker));
        rows["realLocalModelWritesExactReadContent"]=File.Exists(output)&&File.ReadAllText(output)==marker;
        rows["realLocalWriteHasExecutedReceipt"]=conversation.Messages.Any(m=>m.Content.StartsWith("Automatic file action receipt")&&m.Content.Contains("Output.txt"));
        rows["noCloudProviderUsed"]=conversation.Messages.Where(m=>m.Provider!=null).All(m=>m.Provider==profile.Id);
        if(rows.Values.Any(v=>v is false))rows["failureDetail"]=status.Text;
    }
    internal async Task ExerciseChatFileTools(Dictionary<string,object> rows,string root,List<string> packets)
    {
        conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=false;
        var profile=settings.Models.Single();model.SelectedItem=profile;maximum.Text="2048";contextBudget.Text="100000";
        const string request="Read the connected input file and write the requested project outputs.";
        await ExecuteRequest(profile,request,BuildMessages(request),true,false);
        if(packets.Count!=5)rows["requestFailureDetail"]=status.Text;
        rows["ordinaryLocalChatContinuesAcrossFiveModelRequests"]=packets.Count==5;
        rows["localModelReceivesExplicitFileReadCapability"]=packets.Count>0&&packets[0].Contains("You CAN read actual file contents")&&!packets[0].Contains("no automatic execution")&&!packets[0].Contains("Only list/read results explicitly included by the user");
        rows["localModelReceivesActualReadResults"]=packets.Count==5&&packets.Skip(2).All(p=>p.Contains("UNIQUE_INPUT_3287"));
        rows["automaticWriteAndFolderCreationNeedNoReview"]=File.Exists(Path.Combine(root,"generated","Output.txt"))&&File.ReadAllText(Path.Combine(root,"generated","Output.txt"))=="UNIQUE_INPUT_3287 copied";
        rows["automaticOverwriteKeepsOriginalBackup"]=File.ReadAllText(Path.Combine(root,"Existing.txt"))=="updated"&&Directory.GetFiles(Path.Combine(root,".localai-backups")).Any(p=>File.ReadAllText(p)=="original");
        rows["modelReceivesWriteAndReadbackReceipts"]=packets.Count>0&&packets.Last().Contains("Executed")&&packets.Last().Contains("UNIQUE_INPUT_3287 copied");
        rows["finalAnswerAndReceiptsPersist"]=store.Load(out _).Single(c=>c.Id==conversation.Id).Messages.Any(m=>m.Content=="Verified file results received.")&&conversation.Messages.Count(m=>m.Content.StartsWith("Automatic file action receipt"))==3;
        rows["processedToolRequestsDoNotOfferDuplicateExecution"]=!Descendants<System.Windows.Controls.Button>(transcript).Any(b=>Equals(b.Tag,"Review AI file actions"));
        rows["fileActivityCollapsedAndFinalAnswerVisible"]=transcript.Children.OfType<System.Windows.Controls.Expander>().Any(e=>Equals(e.Tag,"FileActivityDetails")&&!e.IsExpanded)&&conversation.Messages.Last().Content=="Verified file results received.";
        await ExecuteRequest(profile,"Truncation fixture",BuildMessages("Truncation fixture"),true,false);
        rows["outputLimitNeverExecutesPartialFileAction"]=!File.Exists(Path.Combine(root,"Truncated.txt"));
        var receiptsBefore=conversation.Messages.Count(m=>m.Content.StartsWith("File tool results"));
        await ExecuteRequest(profile,"Bounded tool fixture",BuildMessages("Bounded tool fixture"),true,false);
        rows["repeatedToolRequestsStopAtConfiguredLimit"]=conversation.Messages.Count(m=>m.Content.StartsWith("File tool results"))-receiptsBefore==ChatFileTools.MaximumRounds&&status.Text.Contains("step limit");
        conversation.ResponseStyle="Plan";
        var plan=await RunChatFileTools([new("write",Path.Combine(root,"Plan.txt"),Content:"bad")],default);
        rows["planModeDoesNotWrite"]=!File.Exists(Path.Combine(root,"Plan.txt"))&&plan.Contains("Plan mode");conversation.ResponseStyle="Build";
        var outside=Path.Combine(Path.GetDirectoryName(root)!,"Outside.txt");
        var denied=await RunChatFileTools([new("write",outside,Content:"bad")],default);
        rows["chatCannotWriteOutsideWorkingFolder"]=!File.Exists(outside)&&denied.Contains("outside the connected");
        var nested=Path.Combine(root,"nested");Directory.CreateDirectory(nested);File.WriteAllText(Path.Combine(nested,"AGENTS.md"),"Use the project naming convention.");
        var nestedFile=Path.Combine(nested,"Scoped.txt");var first=await RunChatFileTools([new("write",nestedFile,Content:"scoped")],default);
        rows["newDirectoryRulesReachModelBeforeWrite"]=!File.Exists(nestedFile)&&first.Contains("instructions were loaded")&&BuildMessages("continue").Any(m=>m.Content.Contains("Use the project naming convention."));
        await RunChatFileTools([new("write",nestedFile,Content:"scoped")],default);
        rows["scopedWriteWorksAfterRulesLoaded"]=File.ReadAllText(nestedFile)=="scoped";
        var cloud=new ModelProfile{Id="cloud-fixture",Engine="codex",Label="Offline cloud fixture"};var packet=BuildMessages("continue");
        var consent=new ProviderConsent(cloud,packet);var handoff=new CliHandoffWindow([cloud],cloud.Id,packet,"offline test");
        rows["cloudConsentDisclosesAutomaticFolderWrites"]=Descendants<System.Windows.Controls.TextBlock>(consent).Any(t=>t.Text.Contains("automatically read and write screened files"));
        rows["cloudHandoffDisclosesAutomaticFolderWrites"]=Descendants<System.Windows.Controls.TextBlock>(handoff).Any(t=>t.Text.Contains("automatically read and write screened files"))&&ChatFileTools.Enabled(handoff.Packet.Messages);
        rows["handoffDetailsCollapsedWithoutChangingReviewedPacket"]=!handoff.TestRequestDetails.IsExpanded&&handoff.Packet.Messages.Any(m=>m.Content.Contains("You CAN read actual file contents"));
        consent.Close();handoff.Close();
        var requestsBefore=packets.Count;prompt.Text="Can you read the files now?";await Send();
        rows["readCapabilityQuestionUsesActualConnectedScopeWithoutModelGuessing"]=packets.Count==requestsBefore&&conversation.Messages.Last().Content.StartsWith("Yes. I can read actual file contents")&&conversation.Messages.Last().Content.Contains(root)&&conversation.Messages.Last().Provider==null;
        var roots=conversation.WorkspaceRoots;conversation.WorkspaceRoots=[];prompt.Text="Can you read the files now?";await Send();
        rows["readCapabilityDoesNotClaimAccessWithoutConnectedFolder"]=packets.Count==requestsBefore&&conversation.Messages.Last().Content.Contains("once a folder is connected");conversation.WorkspaceRoots=roots;store.Save(conversation);
        NewConversation();conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=false;model.SelectedItem=profile;maximum.Text="2048";contextBudget.Text="20000";
        string Receipt(string value)=>ChatContext.ResultPrefix+JsonSerializer.Serialize(new[]{new{status="Executed",Operation="read",Path=Path.Combine(root,"Old.txt"),receipt=new FileActionReceipt("read",Path.Combine(root,"Old.txt"),null,value)}});
        var original=Receipt(new string('z',50000));conversation.Messages.Add(new("user",original));conversation.Messages.Add(new("user",Receipt("Recent small result")));
        var beforePaged=packets.Count;const string pagedRequest="LARGE_PAGE_REVIEW_FIXTURE: Inspect the large ledger and report its latest unfinished item.";
        await ExecuteRequest(profile,pagedRequest,BuildMessages(pagedRequest),true,false);
        rows["largeFilePagesContinueThroughActualChatLoop"]=packets.Count==beforePaged+3&&conversation.Messages.Last().Content=="LARGE_PAGED_REVIEW_DONE";
        rows["largeFileFirstAndTailPagesReachModel"]=packets.Skip(beforePaged).Any(p=>p.Contains("LARGE_BEGIN_MARKER"))&&packets.Last().Contains("LATEST_UNFINISHED_MARKER");
        rows["oversizedPriorFilePayloadReleasedFromWorkingContext"]=packets.Skip(beforePaged).All(p=>p.Contains("workingContextNotice")&&!p.Contains(new string('z',50000)));
        rows["fullPriorFileReceiptStillPersisted"]=store.Load(out _).Single(c=>c.Id==conversation.Id).Messages.Any(m=>m.Content==original);
        if(rows["largeFilePagesContinueThroughActualChatLoop"] is false)rows["pagedFailureDetail"]=status.Text;
    }
}

internal static class ChatFileToolTests
{
    internal static async Task RunLive(string output)
    {
        var rows=new Dictionary<string,object>();MainWindow? window=null;
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"live-working-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var settings=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));settings.Models=[settings.Models.Single(m=>m.Id=="qwen25-coder")];settings.GenerationTimeoutSeconds=240;
            window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"live-window.json")],settings);
            await window.ExerciseLiveChatFileTools(rows,root);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally
        {
            var log=window?.ChatFileTestLog;window?.Close();
            if(log!=null)
            {
                var logs=Path.GetFullPath(Path.Combine(AppPaths.DataRoot,"logs"));
                if(Path.GetDirectoryName(Path.GetFullPath(log))==logs)File.Delete(log);
            }
        }
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));var exit=rows.Count>0&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();MainWindow? window=null;
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"chat-working-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"Input.txt"),"UNIQUE_INPUT_3287");File.WriteAllText(Path.Combine(root,"Existing.txt"),"original");
        File.WriteAllText(Path.Combine(root,"LargeLedger.md"),"LARGE_BEGIN_MARKER\n"+new string('x',200000)+"\nLATEST_UNFINISHED_MARKER");
        var packets=new List<string>();var largeStep=0;using var stop=new CancellationTokenSource();var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;
        string Packet(params FileAction[] actions)=>"```infinity-files\n"+JsonSerializer.Serialize(actions,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})+"\n```";
        string[] replies=["Let me list the key files.\n\n<infinity-files>"+JsonSerializer.Serialize(new[]{new{operation="list",path=root}})+"</infinity-files>","I will read the input file now.\n"+Packet(new FileAction("read",Path.Combine(root,"Input.txt"))),Packet(new FileAction("mkdir",Path.Combine(root,"generated")),new("write",Path.Combine(root,"generated","Output.txt"),Content:"UNIQUE_INPUT_3287 copied"),new("write",Path.Combine(root,"Existing.txt"),Content:"updated")),Packet(new FileAction("read",Path.Combine(root,"generated","Output.txt"))),"Verified file results received."];
        var serving=Task.Run(async()=>
        {
            try{while(!stop.IsCancellationRequested)
            {
                using var connection=await listener.AcceptTcpClientAsync(stop.Token);using var stream=connection.GetStream();
                var header=new List<byte>();var one=new byte[1];
                while(header.Count<32768){if(await stream.ReadAsync(one,stop.Token)==0)throw new EndOfStreamException();header.Add(one[0]);if(header.Count>=4&&Encoding.ASCII.GetString(header.TakeLast(4).ToArray())=="\r\n\r\n")break;}
                var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var lengthLine=lines.FirstOrDefault(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));
                var body=new byte[lengthLine==null?0:int.Parse(lengthLine.Split(':')[1])];await stream.ReadExactlyAsync(body,stop.Token);
                if(lines.Any(l=>l.Equals("Transfer-Encoding: chunked",StringComparison.OrdinalIgnoreCase)))
                {
                    async Task<string> ReadLine(){var bytes=new List<byte>();while(true){await stream.ReadExactlyAsync(one,stop.Token);bytes.Add(one[0]);if(bytes.Count>=2&&bytes[^2]==13&&bytes[^1]==10)return Encoding.ASCII.GetString(bytes.ToArray()[..^2]);}}
                    using var decoded=new MemoryStream();
                    while(true){var size=Convert.ToInt32((await ReadLine()).Split(';')[0],16);if(size==0){await ReadLine();break;}var part=new byte[size];await stream.ReadExactlyAsync(part,stop.Token);decoded.Write(part);await ReadLine();}body=decoded.ToArray();
                }
                string response;
                if(lines[0].Contains("/health"))response=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});
                else
                {
                    lock(packets)packets.Add(Encoding.UTF8.GetString(body));
                    var answer=Encoding.UTF8.GetString(body).Contains("LARGE_PAGE_REVIEW_FIXTURE")?++largeStep switch{1=>Packet(new FileAction("read",Path.Combine(root,"LargeLedger.md"),Length:1000)),2=>Packet(new FileAction("read",Path.Combine(root,"LargeLedger.md"),Offset:-1000,Length:1000)),_=>"LARGE_PAGED_REVIEW_DONE"}:packets.Count<=5?replies[packets.Count-1]:packets.Count==6?Packet(new FileAction("write",Path.Combine(root,"Truncated.txt"),Content:"must not execute")):Packet(new FileAction("list",root));
                    response="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=answer},finish_reason=packets.Count==6?"length":"stop"}}})+"\n\ndata: [DONE]\n\n";
                }
                var bytes=Encoding.UTF8.GetBytes(response);await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nContent-Type: application/json\r\n\r\n"),stop.Token);await stream.WriteAsync(bytes,stop.Token);
            }}catch(OperationCanceledException){}catch(ObjectDisposedException){}
        });
        try
        {
            var settings=new Settings{Models=[new(){Id="file-tool-fixture",Engine="ryzen",ModelPath=root,Endpoint=$"http://127.0.0.1:{port}/",ContextLimit=100000}],GenerationTimeoutSeconds=60};
            window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"isolated-window.json")],settings);
            await window.ExerciseChatFileTools(rows,root,packets);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();stop.Cancel();listener.Stop();await serving;}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        var exit=rows.Count>0&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
}
