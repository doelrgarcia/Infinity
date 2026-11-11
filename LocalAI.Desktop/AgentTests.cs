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
    internal async Task RunAgentLiveTests(Dictionary<string,object> rows,string root)
    {
        var profile=settings.Models.Single(p=>p.Id=="qwen25-coder");var folder=Path.Combine(root,"live-workspace");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"agent-check.txt");var marker="INFINITY_AGENT_"+Guid.NewGuid().ToString("N");File.WriteAllText(path,marker);
        OpenAgent();Agent.Roots=[folder];SaveAgent();
        await agentView!.TestSend("Read the connected file "+path+" using readFile, then tell me its exact contents. Do not create tasks or modify anything.",profile.Id);
        rows["realQwenUsesFileTool"]=Agent.Messages.Any(m=>m.Role=="user"&&m.Content.StartsWith("Untrusted tool result for readFile")&&m.Content.Contains(marker));
        rows["realQwenRepliesFromActualFile"]=Agent.Messages.Any(m=>m.Role=="assistant"&&AgentProtocol.Parse(m.Content) is {Action:"reply"} call&&call.Value.Contains(marker));
        rows["liveRequestDoesNotModifyFile"]=File.ReadAllText(path)==marker&&Agent.Approvals.Count==0;
        File.WriteAllText(Path.Combine(root,"live-transcript.json"),JsonSerializer.Serialize(Agent.Messages,new JsonSerializerOptions{WriteIndented=true}));CloseAgentView();
    }
    internal async Task RunAgentTests(Dictionary<string,object> rows,string root)
    {
        static bool Refuses(Action action){try{action();return false;}catch{return true;}}
        var contextHistory=new List<Message>{new("system","RULES"),new("user","SCOPE"),new("user","ORIGINAL_TASK"),new("assistant",new string('x',4000)),new("user","LATEST_REQUEST"),new("assistant","readFile"),new("user","Untrusted tool result for readFile: CURRENT_RECEIPT")};
        var originalCount=contextHistory.Count;
        var rejectedPath=Path.Combine(root,"Rejected.swift");var rejectedCode="REJECTED_SOURCE_MARKER";
        var declined=new AgentApproval{Kind="files",Status="Declined",Value=JsonSerializer.Serialize(new[]{new{operation="write",path=rejectedPath,content=rejectedCode}})};
        var rejectedMessage=new Message("assistant",JsonSerializer.Serialize(new{action="proposeFile",target=rejectedPath,value="",content=rejectedCode}));
        var oldStatus=new Message("assistant",JsonSerializer.Serialize(new{action="reply",value="Agent needs attention: Agent reached its 10-step limit.",target=rejectedPath,content=rejectedCode}));
        var correction=new Message("user","Correct the compiler findings");var audit=new[]{rejectedMessage,oldStatus,correction};var projected=AgentContext.ForModel(audit,[declined]);
        rows["declinedDraftBodyOmittedFromInferenceWithoutDeletingAudit"]=projected.All(m=>!m.Content.Contains(rejectedCode))&&audit[0].Content.Contains(rejectedCode)&&projected.Last()==correction;
        rows["pendingDraftsAndRealControllerErrorsRemainAvailable"]=AgentContext.ForModel([rejectedMessage,new("assistant","{\"action\":\"reply\",\"value\":\"Real controller error\"}")],[new(){Kind="files",Status="Pending review",Value=declined.Value}]).First()==rejectedMessage&&AgentContext.ForModel([new("assistant","{\"action\":\"reply\",\"value\":\"Real controller error\"}")],[declined]).Single().Content.Contains("Real controller error");
        rows["correctedSourceIsNotMistakenForDeclinedDraft"]=!AgentContext.WasDeclined(new("write",rejectedPath,Content:"Corrected source"),[declined]);
        var fitted=await AgentContext.Fit(contextHistory,1000,(p,_)=>Task.FromResult(p.Sum(m=>m.Content.Length)),CancellationToken.None);
        rows["contextTrimmingPreservesScopeAndUserRequests"]=new[]{"RULES","SCOPE","ORIGINAL_TASK","LATEST_REQUEST"}.All(v=>fitted.Any(m=>m.Content==v));
        rows["contextTrimmingPreservesLatestToolReceipt"]=fitted.Any(m=>m.Content.Contains("CURRENT_RECEIPT"));
        rows["contextTrimmingRetainsOriginalAuditHistory"]=contextHistory.Count==originalCount&&contextHistory[3].Content.Length==4000;
        rows["contextTrimmingIsExplicitAndFitsBudget"]=fitted.Any(m=>m.Content.Contains("history messages omitted"))&&fitted.Sum(m=>m.Content.Length)<=1000;
        var oversizedRefused=false;try{await AgentContext.Fit(contextHistory,1,(p,_)=>Task.FromResult(p.Sum(m=>m.Content.Length)),CancellationToken.None);}catch(IOException){oversizedRefused=true;}rows["oversizedRequiredRequestFailsWithoutDroppingScope"]=oversizedRefused;
        var repairTurn=0;var sawRepair=false;var repairHistory=new List<Message>{new("user","Write source")};
        await AgentProtocol.Run(repairHistory,"",(p,_)=>{sawRepair|=p[0].Content.Contains("one complete file per action");return Task.FromResult(++repairTurn==1?"{\"action\":\"proposeFiles\",\"value\":\"bad batch\"}":"{\"action\":\"reply\",\"value\":\"Nothing applied\"}");},(_,_)=>throw new IOException("Malformed batch"),()=>{},CancellationToken.None);
        rows["failedBatchGuidesSimpleFileRetryWithoutApplying"]=sawRepair&&repairHistory.Any(m=>m.Content.Contains("Tool failed"));
        var schema=AgentProtocol.ResponseSchema;
        rows["agentSchemaConstrainsOneObjectAndKnownStringActions"]=schema.GetProperty("type").GetString()=="object"&&!schema.GetProperty("additionalProperties").GetBoolean()&&schema.GetProperty("properties").GetProperty("action").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()).ToHashSet().SetEquals(AgentProtocol.Tools.Append("reply"))&&schema.GetProperty("required").GetArrayLength()==4;
        rows["protocolRejectsUnknownTools"]=Refuses(()=>AgentProtocol.Parse("{\"action\":\"exec\"}"));
        rows["protocolRejectsDuplicateFields"]=Refuses(()=>AgentProtocol.Parse("{\"action\":\"reply\",\"action\":\"readFile\"}"));
        rows["protocolRejectsUnknownFields"]=Refuses(()=>AgentProtocol.Parse("{\"action\":\"reply\",\"script\":\"oops\"}"));
        rows["protocolRejectsNonStringFields"]=Refuses(()=>AgentProtocol.Parse("{\"action\":\"reply\",\"value\":5}"));
        rows["protocolRejectsMultipleActions"]=Refuses(()=>AgentProtocol.Parse("[{\"action\":\"reply\"}]"));
        rows["protocolAcceptsConversationalReply"]=AgentProtocol.Parse("{\"action\":\"reply\",\"value\":\"Hello\"}").Value=="Hello";
        rows["protocolAcceptsFencedJson"]=AgentProtocol.Parse("```json\n{\"action\":\"listChats\"}\n```").Action=="listChats";
        rows["protocolRejectsOversizeActions"]=Refuses(()=>AgentProtocol.Parse(new string('a',131073)));
        var attempts=0;var effects=0;var history=new List<Message>{new("user","Test")};
        try{await AgentProtocol.Run(history,"",(_,_)=>{attempts++;return Task.FromResult("invalid");},(_,_)=>{effects++;return Task.FromResult("");},()=>{},CancellationToken.None);}catch(IOException){}
        rows["invalidModelOutputBoundedAndNoEffects"]=attempts==10&&effects==0;
        using(var stopped=new CancellationTokenSource())
        {
            var receipts=new List<Message>();try{await AgentProtocol.Run(receipts,"",(_,_)=>Task.FromResult("{\"action\":\"listChats\"}"),(_,_)=>{stopped.Cancel();return Task.FromResult("REAL_OUTCOME");},()=>{},stopped.Token);}catch(OperationCanceledException){}
            rows["cancellationRetainsCompletedToolReceipt"]=receipts.Any(m=>m.Content.Contains("REAL_OUTCOME"));
        }
        var statePath=Path.Combine(root,"state-fixture");var stateStore=new AgentStore(statePath);var state=new AgentState{Memory="Remember my editor preference",Draft="Unsent",Tasks=[new(){Title="Restarted task",Status="Running"}],Approvals=[new(){Kind="command",Value="{}",Status="Reviewing"}]};stateStore.Save(state);var reloaded=new AgentStore(statePath).Load();
        rows["memoryAndDraftPersist"]=reloaded.Memory==state.Memory&&reloaded.Draft=="Unsent";
        rows["restartPausesTasksWithoutReplay"]=reloaded.Paused&&reloaded.Tasks.Single().Status=="Paused";
        rows["restartMarksInFlightApprovalUnknown"]=reloaded.Approvals.Single().Status.StartsWith("Outcome unknown");
        var external=new AgentStore(statePath);var changed=external.Load();changed.Memory="Other window";external.Save(changed);rows["externalStateChangeRefusesOverwrite"]=Refuses(()=>stateStore.Save(state))&&new AgentStore(statePath).Load().Memory=="Other window";
        File.WriteAllText(Path.Combine(statePath,"agent.json"),"{bad");rows["malformedStatePreservedAndFailsClosed"]=Refuses(()=>new AgentStore(statePath).Load())&&File.ReadAllText(Path.Combine(statePath,"agent.json"))=="{bad";
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var shutdown=new CancellationTokenSource();var packets=new List<string>();var slow=false;
        var folder=Path.Combine(root,"connected");Directory.CreateDirectory(folder);var source=Path.Combine(folder,"sample.txt");File.WriteAllText(source,"ACTUAL_FILE_CONTENT");
        var profile=new ModelProfile{Id="agent-local-fixture",Label="Agent test model",Engine="ryzen",ModelPath=root,Endpoint=$"http://127.0.0.1:{port}",ContextLimit=65536};
        string Answer(string packet)
        {
            using var doc=JsonDocument.Parse(packet);var messages=doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();var users=messages.Where(m=>m.GetProperty("role").GetString()=="user").Select(m=>m.GetProperty("content").GetString()!).ToArray();var last=users.Last();
            if(users.Contains("DELEGATED_FIXTURE"))return last.StartsWith("Untrusted tool result")?JsonSerializer.Serialize(new{action="reply",value="A command is proposed. Open Approvals; it has not run."}):JsonSerializer.Serialize(new{action="proposeCommand",value="Write-Output 'AGENT_COMMAND_RECEIPT'",target=folder});
            if(users.Contains("DELEGATE_ROOT_FIXTURE"))return last.StartsWith("Untrusted tool result")?JsonSerializer.Serialize(new{action="reply",value="Your coding task was delegated."}):JsonSerializer.Serialize(new{action="delegate",value="Agent coding fixture",content="DELEGATED_FIXTURE"});
            if(users.Contains("FREEFORM_FIXTURE"))return last.StartsWith("Untrusted tool result")?JsonSerializer.Serialize(new{action="reply",value="I read ACTUAL_FILE_CONTENT from the connected folder."}):JsonSerializer.Serialize(new{action="readFile",value=source});
            return "{\"action\":\"reply\",\"value\":\"A conversational reply.\"}";
        }
        var serving=Task.Run(async()=>{while(!shutdown.IsCancellationRequested){HttpListenerContext c;try{c=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch(Exception e) when(e is HttpListenerException or ObjectDisposedException or OperationCanceledException){break;}try{string body;if(c.Request.Url!.AbsolutePath=="/health")body=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});else if(c.Request.HttpMethod=="GET"){c.Response.ContentType="text/html";body="<html><title>Agent fixture</title><body>VERIFIED_BROWSER_TEXT<a href='/page?next=1'>Next page</a></body></html>";}else{using var reader=new StreamReader(c.Request.InputStream);var packet=await reader.ReadToEndAsync();lock(packets)packets.Add(packet);if(slow)await Task.Delay(700);body="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=Answer(packet)},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));c.Response.Close();}catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){try{c.Response.Abort();}catch(ObjectDisposedException){}}}});
        try
        {
            settings.Models.Add(profile);prompt.Text="MAIN_CHAT_DRAFT";conversation.Messages.Add(new("user","UNRELATED_CHAT_SECRET"));store.Save(conversation);var original=conversation.Id;
            OpenAgent();Agent.Roots=[folder];Agent.Memory="Approved preference";SaveAgent();
            rows["agentHasSeparateConversation"]=agentView!=null&&Agent.Messages.Count==0&&conversation.Id==original;
            await agentView!.TestSend("FREEFORM_FIXTURE",profile.Id);
            rows["freeformModelToolReplyUsesRealFile"]=Agent.Messages.Last().Content.Contains("I read ACTUAL_FILE_CONTENT")&&packets.Count==2;
            rows["agentDoesNotLeakUnselectedChatContext"]=packets.All(p=>!p.Contains("UNRELATED_CHAT_SECRET"));
            rows["agentPreservesMainChatDraft"]=prompt.Text=="MAIN_CHAT_DRAFT"&&conversation.Id==original;
            await modelGate.WaitAsync();var packetCount=packets.Count;var waiting=AgentSend("A queued reply",profile.Id);await Task.Delay(30);rows["sharedModelGateSerializesAgentRequests"]=agentRequest!=null&&packets.Count==packetCount;AgentStopReply();await waiting;modelGate.Release();rows["queuedModelRequestCanBeCancelled"]=agentRequest==null&&packets.Count==packetCount;
            rows["unconnectedFileReadRefused"]=await ToolRefused(new("readFile",Path.Combine(root,"outside.txt")),profile.Id);
            rows["unknownChatIdRefused"]=await ToolRefused(new("readChat",Target:"not-an-id"),profile.Id);
            Agent.Messages.Clear();await AgentSend("DELEGATE_ROOT_FIXTURE",profile.Id);
            for(var n=0;n<200&&(pumping||Agent.Tasks.Count==0);n++)await Task.Delay(20);
            var job=Agent.Tasks.Single();rows["modelDelegatesToIndependentSavedChat"]=job.ChatId!=original&&conversations.Any(c=>c.Id==job.ChatId)&&store.Load(out _).Any(c=>c.Id==job.ChatId);
            rows["agentCanInspectTaskQueueConversationally"]=(await AgentTool(new("listTasks"),profile.Id,null,CancellationToken.None)).Contains(job.Id.ToString());
            rows["backgroundTaskRunsThroughModelAndWaitsForReview"]=job.Status=="Waiting for review"&&Agent.Approvals.Single().Kind=="command";
            rows["pendingCommandHasNotExecuted"]=job.Messages.Any(m=>m.Content.Contains("Pending review"))&&!Directory.Exists(Path.Combine(AgentDirectory,"command-logs"));
            rows["backgroundResponseDoesNotClaimProjectComplete"]=conversations.Single(c=>c.Id==job.ChatId).ActivityDetail.Contains("not independently verified");
            rows["backgroundTasksCannotRecursivelyDelegate"]=await ToolRefused(new("delegate","Recursive",Content:"Do more"),profile.Id,job);
            rows["backgroundTasksCannotSwitchForegroundChat"]=await ToolRefused(new("openChat",Target:original.ToString()),profile.Id,job)&&conversation.Id==original;
            rows["pendingReviewBlocksTaskResume"]=Refuses(()=>AgentResume(job));
            var command=Agent.Approvals.Single();await AgentReview(command,true);rows["declinedApprovalRecordedWithoutExecution"]=command.Status=="Declined"&&job.Messages.Last().Content.Contains("User declined");
            var writePath=Path.Combine(folder,"new.txt");await AgentTool(new("proposeFiles",JsonSerializer.Serialize(new[]{new{operation="write",path=writePath,content="reviewed"}})),profile.Id,null,CancellationToken.None);rows["fileProposalNeverWritesBeforeReview"]=!File.Exists(writePath)&&Agent.Approvals.Last().Kind=="files";
            var fileApproval=Agent.Approvals.Last();agentFileReviewTest=async review=>{await review.TestPreview();await review.TestApply();};await AgentReview(fileApproval);agentFileReviewTest=null;rows["reviewedAgentFileActionWritesAndRecordsReceipt"]=File.Exists(writePath)&&File.ReadAllText(writePath)=="reviewed"&&Agent.Messages.Last().Content.Contains("new.txt",StringComparison.Ordinal);
            rows["finishedApprovalCannotReplay"]=await ReviewRefused(fileApproval);
            await AgentTool(new("proposeCommand","Write-Output 'AGENT_COMMAND_RECEIPT'",folder),profile.Id,job,CancellationToken.None);var runApproval=Agent.Approvals.Last();
            agentCommandReviewTest=async review=>{review.TestDraft("Write-Output 'AGENT_COMMAND_RECEIPT'");await review.TestExecute();review.Close();};await AgentReview(runApproval);agentCommandReviewTest=null;rows["reviewedAgentCommandRunsAndRecordsActualOutput"]=job.Messages.Last().Content.Contains("AGENT_COMMAND_RECEIPT")&&job.Messages.Last().Content.Contains("ExitCode");
            Agent.TrustedWork=true;Agent.TrustedDeletes=0;
            var trustedWrite=Path.Combine(folder,"trusted.txt");await AgentTool(new("proposeFiles",JsonSerializer.Serialize(new[]{new{operation="write",path=trustedWrite,content="trusted"}})),profile.Id,null,CancellationToken.None);rows["trustedFileWriteAppliesWithoutReview"]=File.Exists(trustedWrite)&&File.ReadAllText(trustedWrite)=="trusted"&&!Agent.Approvals.Any(a=>a.Status=="Pending review"&&a.Kind=="files");
            await AgentTool(new("proposeCommand","Write-Output 'TRUSTED_COMMAND'",folder),profile.Id,null,CancellationToken.None);rows["trustedSafeCommandRunsWithoutReview"]=Agent.Approvals.Any(a=>a.Kind=="command"&&a.Status=="Auto-run (trusted)"&&a.Value.Contains("TRUSTED_COMMAND"))&&!Agent.Approvals.Any(a=>a.Kind=="command"&&a.Status=="Pending review");
            await AgentTool(new("proposeCommand","Remove-Item '"+trustedWrite+"'",folder),profile.Id,null,CancellationToken.None);var destructiveApproval=Agent.Approvals.Last();rows["trustedDestructiveCommandStillRequiresReview"]=destructiveApproval.Kind=="command"&&destructiveApproval.Status=="Pending review"&&File.Exists(trustedWrite);await AgentReview(destructiveApproval,true);
            var firstDelete=Path.Combine(folder,"trusted-delete.txt");File.WriteAllText(firstDelete,"x");await AgentTool(new("proposeFiles",JsonSerializer.Serialize(new[]{new{operation="delete",path=firstDelete}})),profile.Id,null,CancellationToken.None);rows["trustedSingleDeleteUsesRecoveryWithoutReview"]=!File.Exists(firstDelete)&&Agent.TrustedDeletes==1&&Agent.Changes.Any(c=>c.Plan.Action.Operation=="delete"&&c.Plan.Source==firstDelete&&c.Status=="Applied");
            var massOne=Path.Combine(folder,"mass1.txt");var massTwo=Path.Combine(folder,"mass2.txt");File.WriteAllText(massOne,"1");File.WriteAllText(massTwo,"2");await AgentTool(new("proposeFiles",JsonSerializer.Serialize(new[]{new{operation="delete",path=massOne},new{operation="delete",path=massTwo}})),profile.Id,null,CancellationToken.None);var massApproval=Agent.Approvals.Last();rows["trustedBatchDeleteStillRequiresReview"]=massApproval.Kind=="files"&&massApproval.Status=="Pending review"&&File.Exists(massOne)&&File.Exists(massTwo);await AgentReview(massApproval,true);
            Agent.Messages.Clear();await AgentSend("TRUSTED_PROMPT_FIXTURE",profile.Id);lock(packets)rows["trustedAgentPromptExplainsAutonomy"]=packets.Any(p=>p.Contains("Trusted work is ON"));
            Agent.TrustedWork=false;var offPath=Path.Combine(folder,"trusted-off.txt");await AgentTool(new("proposeFiles",JsonSerializer.Serialize(new[]{new{operation="write",path=offPath,content="off"}})),profile.Id,null,CancellationToken.None);rows["trustedOffRestoresReview"]=!File.Exists(offPath)&&Agent.Approvals.Last().Kind=="files"&&Agent.Approvals.Last().Status=="Pending review";await AgentReview(Agent.Approvals.Last(),true);
            var memory=Agent.Memory;await AgentTool(new("remember","Proposed replacement"),profile.Id,null,CancellationToken.None);rows["modelMemoryChangesRequireReview"]=Agent.Memory==memory&&Agent.Approvals.Last().Kind=="memory";
            await AgentTool(new("watchChat",Target:original.ToString()),profile.Id,null,CancellationToken.None);var before=Agent.Messages.Count;PollAgentWatches();rows["unchangedWatchStaysQuiet"]=Agent.Messages.Count==before;
            ChatMonitor.Record(conversation,"Needs attention","A real failure fixture",true);PollAgentWatches();var after=Agent.Messages.Count;PollAgentWatches();rows["watchReportsMeaningfulChangeOnce"]=after==before+1&&Agent.Messages.Count==after&&Agent.Messages.Last().Content.Contains("real failure fixture");
            AgentPause();ChatMonitor.Record(conversation,"Stopped","Paused watch fixture");before=Agent.Messages.Count;PollAgentWatches();rows["pauseStopsWatchUpdates"]=Agent.Messages.Count==before&&Agent.Paused;
            AgentResumeWatches();rows["resumingWatchesDoesNotResumePausedWork"]=Agent.Paused&&!Agent.WatchesPaused;
            slow=true;Agent.Messages.Clear();var request=AgentSend("Please answer",profile.Id);for(var n=0;n<100&&agentRequest!=null&&packets.Count<7;n++)await Task.Delay(10);AgentStopReply();await request;rows["stopReplyCancelsOwnedGeneration"]=agentRequest==null&&Agent.Messages.Last().Content.Contains("stopped");slow=false;
            var newJob=new AgentTask{Title="Cancellation fixture",ModelId=profile.Id,ChatId=job.ChatId,Messages=[new("user","Please answer")],Status="Queued"};Agent.Tasks.Add(newJob);Agent.Paused=false;slow=true;var pump=PumpAgentTasks();await Task.Delay(100);AgentCancelTask(newJob);await pump;rows["stopBackgroundTaskPausesWithoutReplay"]=newJob.Status=="Paused";AgentResume(newJob);Close();for(var n=0;n<200&&pumping;n++)await Task.Delay(20);rows["closingAppPausesQueueBeforeCancelling"]=IsVisible&&Agent.Paused&&newJob.Status=="Paused";slow=false;AgentResume(newJob);for(var n=0;n<200&&pumping;n++)await Task.Delay(20);rows["explicitResumeContinuesPausedTask"]=newJob.Status=="Response ready";
            var cloud=new ModelProfile{Id="cloud-agent-fixture",Label="Cloud fixture",Engine="codex",Executable=typeof(MainWindow).Assembly.Location,ContextLimit=8192};settings.Models.Add(cloud);before=packets.Count;
            _=Dispatcher.BeginInvoke(()=>{Application.Current.Windows.OfType<ProviderConsent>().Single().Close();});try{await AgentGenerate(cloud.Id,[new("user","Do not send")],CancellationToken.None);rows["cloudRequiresReviewBeforeEveryRequest"]=false;}catch(OperationCanceledException){rows["cloudRequiresReviewBeforeEveryRequest"]=packets.Count==before;}
            rows["unsafeBrowserSchemeRefused"]=await ToolRefused(new("browserOpen","file:///C:/Windows/win.ini"),profile.Id);
            await AgentTool(new("browserOpen",$"http://127.0.0.1:{port}/page"),profile.Id,null,CancellationToken.None);
            for(var n=0;n<200&&agentBrowser?.Current?.CoreWebView2?.DocumentTitle!="Agent fixture";n++)await Task.Delay(30);
            var page=await AgentTool(new("browserRead"),profile.Id,null,CancellationToken.None);rows["agentReadsActualInAppBrowserPage"]=page.Contains("VERIFIED_BROWSER_TEXT")&&page.Contains("Untrusted");
            var links=await AgentTool(new("browserLinks"),profile.Id,null,CancellationToken.None);using(var doc=JsonDocument.Parse(links)){var id=doc.RootElement[0].GetProperty("id").GetString()!;await AgentTool(new("browserFollow",id),profile.Id,null,CancellationToken.None);}for(var n=0;n<100&&!agentBrowser!.Current!.CoreWebView2.Source.Contains("next=1");n++)await Task.Delay(20);rows["agentFollowsOnlyListedBrowserLink"]=agentBrowser!.Current!.CoreWebView2.Source.Contains("next=1")&&await ToolRefused(new("browserFollow","invented-id"),profile.Id);
            agentView!.TestDraft("Preserved Agent draft");SaveAgent();agentView.Refresh();QaCapture.Save(agentView,Path.ChangeExtension(smokeOutput!,".agent.png"));rows["agentTranscriptShowsConversationalReply"]=agentView.TestTranscript.Contains("Agent:");rows["agentViewFitsWorkArea"]=agentView.ActualWidth<=SystemParameters.WorkArea.Width&&agentView.ActualHeight<=SystemParameters.WorkArea.Height&&Window.GetWindow(agentView)==this;
            CloseAgentView();OpenAgent();rows["agentReopensSameOngoingSession"]=Agent.Draft=="Preserved Agent draft"&&Agent.Tasks.Count==2;
        }
        finally{AgentPause();agentBrowser?.Close();CloseAgentView();shutdown.Cancel();listener.Close();await serving;}
    }
    private async Task<bool> ToolRefused(AgentCall call,string modelId,AgentTask? job=null){try{await AgentTool(call,modelId,job,CancellationToken.None);return false;}catch(Exception e) when(e is IOException or InvalidDataException){return true;}}
    private async Task<bool> ReviewRefused(AgentApproval approval){try{await AgentReview(approval);return false;}catch(IOException){return true;}}
}
internal static class AgentTests
{
    internal static async Task Run(string output,bool live=false)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"agent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);MainWindow? window=null;
        try{window=new MainWindow(["--ui-session",Path.Combine(root,"session.json")]);window.Show();if(live)await window.RunAgentLiveTests(rows,root);else await window.RunAgentTests(rows,root);}catch(Exception e){rows["error"]=e.ToString();}finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Values.All(v=>v is true)?0:1);
    }
}
