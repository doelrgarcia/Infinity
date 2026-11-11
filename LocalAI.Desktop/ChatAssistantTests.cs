using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    internal async Task RunChatAssistantTests(Dictionary<string,object> rows,string root)
    {
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var shutdown=new CancellationTokenSource();var packets=new List<string>();var slow=false;var fail=false;
        var profile=new ModelProfile{Id="chat-agent-qa",Label="Local chat assistant fixture",Engine="ryzen",Endpoint=$"http://127.0.0.1:{port}",ModelPath=root,ContextLimit=32768};
        var serving=Task.Run(async()=>{while(!shutdown.IsCancellationRequested){HttpListenerContext context;try{context=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch(Exception e) when(e is OperationCanceledException or HttpListenerException or ObjectDisposedException){break;}try{var health=context.Request.Url!.AbsolutePath=="/health";string body;if(health)body=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});else{using var reader=new StreamReader(context.Request.InputStream);var packet=await reader.ReadToEndAsync();lock(packets)packets.Add(packet);if(slow)await Task.Delay(1200);if(fail){context.Response.StatusCode=503;body="failure";}else body="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content="1. Review the parser. 2. Add a regression test. 3. Rerun the build."},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}var bytes=Encoding.UTF8.GetBytes(body);await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();}catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){try{context.Response.Abort();}catch(ObjectDisposedException){}}}});
        try
        {
            var legacy=JsonSerializer.Deserialize<Conversation>("{\"Title\":\"Legacy\",\"Messages\":[]}")!;rows["legacyChatsAreNotAssumedComplete"]=ChatMonitor.Describe(legacy,Guid.Empty,false).State=="Not tracked";
            ChatMonitor.Record(legacy,"Working","Old request");rows["savedRunningStateNotClaimedLive"]=!ChatMonitor.Describe(legacy,Guid.Empty,false).CanStop&&ChatMonitor.Describe(legacy,Guid.Empty,false).State.Contains("not verified live");
            rows["onlyCurrentLiveOperationCanStop"]=ChatMonitor.Describe(legacy,legacy.Id,true).CanStop&&!ChatMonitor.Describe(legacy,Guid.NewGuid(),true).CanStop;
            ChatMonitor.Record(legacy,"Needs attention",new string('x',1000),true);store.Save(legacy);var saved=store.Load(out _).Single(c=>c.Id==legacy.Id);rows["attentionAndBoundedDetailPersist"]=saved.NeedsAttention&&saved.ActivityDetail.Length==400&&saved.LastActivityUtc!=null;
            rows["naturalAssistantQueriesRouteLocally"]=AppControlProtocol.FromPrompt("Monitor my chats")?.Action=="chatAssistant"&&AppControlProtocol.FromPrompt("Which chats need attention?")?.Action=="attentionChats";
            settings.Models.Add(profile);model.ItemsSource=null;model.ItemsSource=settings.Models;NewConversation();conversation.Title="Other chat";conversation.Messages.Add(new("user","OTHER_CHAT_PRIVATE_MARKER"));store.Save(conversation);var other=conversation;
            NewConversation();conversation.Title="Agent target";conversation.ModelId=profile.Id;conversation.AutoCliFallback=false;conversation.Messages.Add(new("user","Explain the parser."));store.Save(conversation);var target=conversation;model.SelectedItem=profile;maximum.Text="128";contextBudget.Text="32768";prompt.Text="Unsent target draft";
            OpenChatAssistant();rows["assistantWindowOpensWithoutInference"]=chatAssistant!=null&&packets.Count==0;
            Descendants<Button>(chatAssistant!).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Toggle chat attention").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));chatAssistant!.TestRefresh();rows["attentionFilterCountReflectsPersistedFlag"]=chatAssistant.TestSummary.Contains("1 need attention")&&store.Load(out _).Single(c=>c.Id==target.Id).NeedsAttention;
            rows["attentionFilterShowsOnlyFlaggedChats"]=chatAssistant.TestAttentionFilter();
            await ChatAssistantAction("flag",target.Id,"");rows["acknowledgingDoesNotInventCompletion"]=!target.NeedsAttention&&target.ActivityState=="Not tracked";
            await ChatAssistantAction("rename",target.Id,"Parser work");rows["assistantRenamePersists"]=target.Title=="Parser work"&&store.Load(out _).Single(c=>c.Id==target.Id).Title=="Parser work";
            await ChatAssistantAction("open",other.Id,"");await ChatAssistantAction("open",target.Id,"");rows["assistantSwitchRestoresDraft"]=conversation.Id==target.Id&&prompt.Text=="Unsent target draft";
            prompt.Text="Which chats need attention?";await Send();rows["statusQueryDoesNotUseModelOrOtherContent"]=packets.Count==0&&!target.Messages.Last().Content.Contains("OTHER_CHAT_PRIVATE_MARKER");prompt.Text="Unsent target draft";
            slow=true;var suggestion=ChatAssistantAction("suggest",target.Id,"");for(var n=0;n<100&&packets.Count==0;n++)await Task.Delay(20);chatAssistant.TestRefresh();rows["monitorSeesActualInFlightGeneration"]=busy&&ChatSnapshot().Single(r=>r.Id==target.Id).CanStop&&target.ActivityState=="Working";
            rows["assistantEntryAvailableWhileWorking"]=chatAssistantEntry?.IsEnabled==true;
            try{await ChatAssistantAction("open",other.Id,"");rows["switchBlockedUntilOperationStops"]=false;}catch(IOException){rows["switchBlockedUntilOperationStops"]=conversation.Id==target.Id;}
            await suggestion;rows["suggestionsUseNormalLocalModelAndSelectedContext"]=packets.Count==1&&packets[0].Contains("Explain the parser")&&!packets[0].Contains("OTHER_CHAT_PRIVATE_MARKER")&&target.Messages.Last().Content.Contains("Review the parser");
            rows["suggestionsPreserveUnsentDraft"]=prompt.Text=="Unsent target draft";
            rows["successfulResponseRecordedWithoutProjectCompletionClaim"]=target.ActivityState=="Response ready"&&target.ActivityDetail.Contains("does not certify")&&store.Load(out _).Single(c=>c.Id==target.Id).ActivityState=="Response ready";
            var stopping=ChatAssistantAction("suggest",target.Id,"");for(var n=0;n<100&&!busy;n++)await Task.Delay(20);await ChatAssistantAction("stop",target.Id,"");try{await stopping;}catch(IOException){}rows["assistantStopCancelsOwnedRequestAndKeepsDraft"]=!busy&&target.ActivityState=="Stopped"&&target.NeedsAttention&&prompt.Text=="Unsent target draft";await Task.Delay(1300);
            slow=false;fail=true;try{await ChatAssistantAction("suggest",target.Id,"");}catch(IOException){}rows["failedSuggestionNeedsAttention"]=target.ActivityState=="Needs attention"&&target.NeedsAttention&&prompt.Text=="Unsent target draft";
            try{await ChatAssistantAction("stop",other.Id,"");rows["assistantCannotStopInactiveOtherChat"]=false;}catch(IOException){rows["assistantCannotStopInactiveOtherChat"]=true;}
            var cloud=new ModelProfile{Id="cloud-chat-agent-fixture",Label="Synthetic cloud advisor",Engine="codex",Executable=typeof(MainWindow).Assembly.Location,ContextLimit=8192};settings.Models.Add(cloud);model.ItemsSource=null;model.ItemsSource=settings.Models;target.ModelId=cloud.Id;model.SelectedItem=cloud;var beforeMessages=target.Messages.Count;var beforePackets=packets.Count;
            _=Dispatcher.BeginInvoke(new Action(async()=>{var dialog=Application.Current.Windows.OfType<ProviderConsent>().Single();try{await ChatAssistantAction("open",other.Id,"");rows["chatSwitchBlockedDuringCloudConsent"]=false;}catch(IOException){rows["chatSwitchBlockedDuringCloudConsent"]=conversation.Id==target.Id&&ChatSnapshot().Single(r=>r.Id==target.Id).State=="Waiting for review";}finally{dialog.Close();}}));try{await ChatAssistantAction("suggest",target.Id,"");}catch(IOException){}
            rows["cloudSuggestionsCancelBeforeSending"]=target.Messages.Count==beforeMessages&&packets.Count==beforePackets&&prompt.Text=="Unsent target draft"&&status.Text.Contains("cancelled before sending");
            var external=new ConversationStore(Path.Combine(root,"interactive-conversations"));var externallyChanged=external.Load(out _).Single(c=>c.Id==other.Id);externallyChanged.Title="External change retained";external.Save(externallyChanged);
            try{await ChatAssistantAction("flag",other.Id,"");rows["assistantRefusesExternallyChangedChats"]=false;}catch(IOException){rows["assistantRefusesExternallyChangedChats"]=external.Load(out _).Single(c=>c.Id==other.Id).Title=="External change retained";}
            await ChatAssistantAction("new",null,"Agent-created chat");rows["assistantCreatesIndependentSavedChat"]=conversation.Title=="Agent-created chat"&&conversation.Messages.Count==0&&conversation.WorkspaceRoots.Count==0&&store.Load(out _).Any(c=>c.Id==conversation.Id);
            chatAssistant.TestRefresh();QaCapture.Save(chatAssistant,Path.ChangeExtension(smokeOutput!,".assistant.png"));rows["assistantDarkRender"]=true;chatAssistant.Close();
        }
        finally{shutdown.Cancel();listener.Close();await serving;}
    }
}
internal static class ChatAssistantTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"chat-assistant-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);MainWindow? window=null;
        try{window=new MainWindow(["--ui-session",Path.Combine(root,"session.json")]);window.Show();await window.RunChatAssistantTests(rows,root);}catch(Exception e){rows["error"]=e.ToString();}finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==26&&rows.Values.All(v=>v is true)?0:1);
    }
}
