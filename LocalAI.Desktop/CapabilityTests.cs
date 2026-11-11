using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task ExerciseCapabilities(Dictionary<string,object> rows)
    {
        static bool Refused(Action action){try{action();return false;}catch(IOException){return true;}}
        chatProjectFilter=null;NewConversation();var root=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"capabilities-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var skillDirectory=Path.Combine(root,".agents","skills","check");Directory.CreateDirectory(skillDirectory);var skillPath=Path.Combine(skillDirectory,"SKILL.md");
        File.WriteAllText(skillPath,"---\nname: check\ndescription: Review the project checks\n---\nSKILL_BODY_MARKER: inspect actual receipts; propose scripts for review.");
        var project=CreateChatProject("Capability fixture",root,"");
        rows["skillsCatalogDoesNotActivateUninvokedBody"]=SkillLibrary.ForRequest([root],"hello") is {} catalog&&catalog.Content.Contains("check: Review")&&!catalog.Content.Contains("SKILL_BODY_MARKER");
        rows["explicitSkillInvocationLoadsBodyAndProvenance"]=BuildMessages("$check review").Any(m=>m.Role=="system"&&m.Content.Contains("SKILL_BODY_MARKER")&&m.Content.Contains(skillPath));
        rows["skillScopeDoesNotLeakToUnconnectedProjects"]=SkillLibrary.Discover([]).Count==0&&SkillLibrary.ForRequest([root],"$checker")==SkillLibrary.ForRequest([root],"hello");
        prompt.Text="KEEP_DRAFT";_=Dispatcher.BeginInvoke(()=>{var window=Application.Current.Windows.OfType<SkillsWindow>().Single();Descendants<Button>(window).Single(b=>Equals(b.Content,"Use in prompt")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));});OpenChatSkills();
        rows["nativeSkillChooserAddsInvocationWithoutSending"]=prompt.Text=="KEEP_DRAFT\n$check "&&conversation.Messages.Count==0;
        File.WriteAllText(skillPath,"---\nname: bad name\ndescription: Bad\n---\nbody");rows["malformedSkillRefusesBeforeModelRequest"]=Refused(()=>BuildMessages("test"));File.WriteAllText(skillPath,"---\nname: check\ndescription: Review checks\n---\nSKILL_BODY_MARKER");
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);var png=stream.ToArray();
        var image=ImageInput.FromBytes("Fixture.png",png);AddImage(image);var imageChat=conversation;prompt.Text="Review screenshot";
        rows["screenshotAppearsInNativeDraftAndPacket"]=imageTray.Children.Count==1&&BuildMessages(prompt.Text)[^1].Images?.Single().Sha256==image.Sha256;
        var textProfile=settings.Models.First(p=>!p.IsCloud);model.SelectedItem=textProfile;await Send();rows["textOnlyProviderRefusesWithoutDroppingImageDraft"]=conversation.Messages.Count==0&&DraftImages.Count==1&&prompt.Text=="Review screenshot"&&status.Text.Contains("Screenshots currently require");
        rows["imageFingerprintAndTypeTamperingRefused"]=Refused(()=>(image with {Sha256="changed"}).Bytes())&&Refused(()=>ImageInput.FromBytes("invalid.png",Encoding.UTF8.GetBytes("not really a png image")));
        rows["screenshotCountAndSizeAreBounded"]=Refused(()=>ImageInput.All(Enumerable.Range(0,5).Select(n=>new Message("user","",Images:[image with {Sha256=n.ToString()}]))))&&Refused(()=>ImageInput.FromBytes("large.png",new byte[ImageInput.MaximumBytes+1]));
        NewConversation();history.SelectedItem=imageChat;rows["switchingChatsPreservesImageAndTextDraft"]=DraftImages.Single()==image&&prompt.Text=="Review screenshot";
        var cloud=new ModelProfile{Id="image-codex-fixture",Label="Codex image fixture",Engine="codex",Executable=Environment.ProcessPath!};settings.Models.Add(cloud);model.Items.Refresh();model.SelectedItem=cloud;
        var previewed=false;_=Dispatcher.BeginInvoke(()=>{var consent=Application.Current.Windows.OfType<ProviderConsent>().Single();previewed=Descendants<Image>(consent).Any()&&Descendants<TextBox>(consent).Any(t=>t.Text.Contains(image.Sha256));consent.Close();});await Send();
        rows["cloudImageConsentShowsExactImageAndCancelSendsNothing"]=previewed&&conversation.Messages.Count==0&&DraftImages.Count==1&&status.Text.Contains("cancelled before sending");
        var snapshot=Path.Combine(root,"reviewed.png");File.WriteAllBytes(snapshot,png);var start=CliAssistant.CreateStart(cloud,root,"unused","reviewed text",[snapshot]);
        rows["codexImageArgumentsRetainReadOnlyAndStdinControls"]=start.ArgumentList.Contains("--image")&&start.ArgumentList.Contains(snapshot)&&start.ArgumentList.Contains("--ignore-user-config")&&start.ArgumentList.Contains("read-only")&&start.ArgumentList.Contains("shell_tool")&&start.ArgumentList.Last()=="-"&&!start.Arguments.Contains("reviewed text");
        var packet=CliEscalation.Prepare([new("system","CONTROLLER"),new("system","FULL_GUIDANCE"),new("user",new string('x',20000)),new("user","Review",Images:[image])],"fixture");
        rows["handoffRetainsFullInstructionsAndImageSnapshot"]=packet.Messages.Any(m=>m.Content=="FULL_GUIDANCE")&&ImageInput.All(packet.Messages).Single()==image&&packet.Prompt.Contains(image.Sha256)&&packet.Prompt.Length<=CliAssistant.MaximumPromptCharacters;
        rows["handoffRefusesOversizedInstructionsInsteadOfClipping"]=Refused(()=>CliEscalation.Prepare([new("system","controller"),new("system",new string('x',CliAssistant.MaximumPromptCharacters)),new("user","request")],"fixture"));
        imageChat.Messages.Add(new("user","Saved image",Images:[image]));store.Save(imageChat);rows["sentImageSnapshotsPersistWithConversation"]=store.Load(out _).Single(c=>c.Id==imageChat.Id).Messages.Single().Images?.Single().Sha256==image.Sha256;
        DraftImages.Clear();prompt.Clear();NewConversation();
        await ExerciseLiveFollowUps(rows,root);
        await ExerciseIsolatedWorkspace(rows,root);
    }
    private async Task ExerciseLiveFollowUps(Dictionary<string,object> rows,string root)
    {
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var shutdown=new CancellationTokenSource();
        var started=new TaskCompletionSource();var release=new TaskCompletionSource();var packets=new List<string>();var calls=0;var streamStarted=new TaskCompletionSource();var streamRelease=new TaskCompletionSource();var streamed=false;
        var serving=Task.Run(async()=>{while(!shutdown.IsCancellationRequested){HttpListenerContext context;try{context=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch(Exception e) when(e is OperationCanceledException or HttpListenerException or ObjectDisposedException){break;}try{string data;if(context.Request.Url!.AbsolutePath=="/health")data=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});else{using var reader=new StreamReader(context.Request.InputStream);var packet=await reader.ReadToEndAsync();lock(packets)packets.Add(packet);var n=Interlocked.Increment(ref calls);if(packet.Contains("STREAM_FOR_STEERING")&&!streamed){streamed=true;var partial="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content="PARTIAL_BEFORE_STEERING"},finish_reason=(string?)null}}})+"\n\n";await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(partial));await context.Response.OutputStream.FlushAsync();streamStarted.TrySetResult();await streamRelease.Task.WaitAsync(shutdown.Token);context.Response.Close();continue;}if(n==1){started.TrySetResult();await release.Task.WaitAsync(shutdown.Token);}var action=n==1?new AgentCall("listTasks"):new AgentCall("reply","LIVE_REPLY_"+n);data="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=JsonSerializer.Serialize(action,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(data));context.Response.Close();}catch(Exception e) when(e is IOException or OperationCanceledException or HttpListenerException or ObjectDisposedException){context.Response.Abort();}}});
        var profile=new ModelProfile{Id="live-followup",Label="Live follow-up fixture",Engine="ryzen",Endpoint=$"http://127.0.0.1:{port}",ModelPath=root,ContextLimit=131072};settings.Models.Add(profile);model.Items.Refresh();model.SelectedItem=profile;
        var chat=conversation;chat.ModelId=profile.Id;chat.Messages.Add(new("user","Original follow-up task"));store.Save(chat);var job=new AgentTask{Title="Live task",Instructions="Original follow-up task",ModelId=profile.Id,ChatId=chat.Id,Messages=[..chat.Messages]};Agent.Tasks.Add(job);Agent.Paused=false;SaveAgent();var count=Agent.Tasks.Count;
        try
        {
            var running=PumpAgentTasks();await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AgentFollowUp(job,"STEER_NEXT_SAFE_STEP","Steer");AgentFollowUp(job,"QUEUE_AFTER_REPLY","Queue");
            rows["runningFollowUpsPersistWithoutDuplicateTasksOrEarlyExecution"]=job.FollowUps.Count==2&&job.Messages.Count==1&&Agent.Tasks.Count==count&&new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==job.Id).FollowUps.All(f=>f.Status=="Pending");
            release.TrySetResult();await running.WaitAsync(TimeSpan.FromSeconds(15));
            rows["steerReachesNextModelStepBeforeQueuedFollowUp"]=packets.Count==3&&packets[1].Contains("STEER_NEXT_SAFE_STEP")&&!packets[1].Contains("QUEUE_AFTER_REPLY")&&packets[2].Contains("QUEUE_AFTER_REPLY");
            rows["liveFollowUpsApplyExactlyOnceAndPreserveToolReceipt"]=job.FollowUps.All(f=>f.Status=="Applied")&&job.Messages.Count(m=>m.Content=="STEER_NEXT_SAFE_STEP")==1&&chat.Messages.Count(m=>m.Content=="QUEUE_AFTER_REPLY")==1&&job.Messages.Any(m=>m.Content.StartsWith("Untrusted tool result for listTasks"));
            job.Status="Running";AgentFollowUp(job,"PAUSED_FOLLOWUP");job.Status="Paused";SaveAgent();var loaded=new AgentStore(AgentDirectory).Load();rows["restartLeavesQueuedFollowUpsPausedWithoutReplay"]=loaded.Paused&&loaded.Tasks.Single(t=>t.Id==job.Id).FollowUps.Last().Status=="Pending";
            SetBusy(true);prompt.Text="SAVED_CHAT_FOLLOWUP";await Send();SetBusy(false);rows["busyChatComposerQueuesPersistedFollowUpWithoutSending"]=prompt.Text.Length==0&&chat.FollowUps.Last().Text=="SAVED_CHAT_FOLLOWUP"&&chat.Messages.All(m=>m.Content!="SAVED_CHAT_FOLLOWUP")&&store.Load(out _).Single(c=>c.Id==chat.Id).FollowUps.Single().Status=="Pending";
            await DrainChatFollowUps();rows["savedChatFollowUpActuallyRunsOnceAndMarksApplied"]=chat.FollowUps.Single().Status=="Applied"&&chat.Messages.Count(m=>m.Content=="SAVED_CHAT_FOLLOWUP")==1&&packets.Last().Contains("SAVED_CHAT_FOLLOWUP");
            SetBusy(true);prompt.Text="STOP_BEFORE_NEXT_SEND";await Send();SetBusy(false);await modelGate.WaitAsync();var cancelled=DrainChatFollowUps();active!.Cancel();modelGate.Release();await cancelled;
            rows["stoppedQueuedChatSendStaysPendingAndRetainsDraft"]=chat.FollowUps.Last().Status=="Pending"&&prompt.Text=="STOP_BEFORE_NEXT_SEND"&&chat.Messages.All(m=>m.Content!="STOP_BEFORE_NEXT_SEND");
            await DrainChatFollowUps();rows["explicitContinueResumesStoppedChatFollowUpExactlyOnce"]=chat.FollowUps.Last().Status=="Applied"&&chat.Messages.Count(m=>m.Content=="STOP_BEFORE_NEXT_SEND")==1;
            prompt.Text="STREAM_FOR_STEERING";var streaming=Send();await streamStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));for(var i=0;i<100&&!Descendants<TextBox>(transcript).Any(t=>t.Text.Contains("PARTIAL_BEFORE_STEERING"));i++)await Task.Delay(10);
            chatFollowMode.SelectedItem="Steer";prompt.Text="CHAT_STEER_CORRECTION";await Send();streamRelease.TrySetResult();await streaming.WaitAsync(TimeSpan.FromSeconds(15));
            rows["mainChatSteeringInterruptsOwnedStreamAndSendsCorrectionOnce"]=chat.Messages.Any(m=>m.Role=="assistant"&&m.Content.Contains("PARTIAL_BEFORE_STEERING")&&m.Content.Contains("Interrupted"))&&chat.Messages.Count(m=>m.Content=="CHAT_STEER_CORRECTION")==1&&chat.FollowUps.Last().Status=="Applied"&&packets.Last().Contains("CHAT_STEER_CORRECTION");
            chatFollowMode.SelectedItem="Queue";job.FollowUps.Last().Status="Applied";chat.FollowUps.Clear();store.Save(chat);SaveAgent();
        }
        finally{streamRelease.TrySetResult();release.TrySetResult();AgentPause();shutdown.Cancel();listener.Stop();await serving;}
    }
    private async Task ExerciseIsolatedWorkspace(Dictionary<string,object> rows,string root)
    {
        var repo=Path.Combine(root,"git-project");Directory.CreateDirectory(repo);var git=SourceControl.FindTool(SourceProvider.Git)??throw new IOException("Git fixture requires Git for Windows.");
        async Task Git(params string[] args){var start=new System.Diagnostics.ProcessStartInfo(git){WorkingDirectory=repo,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in args)start.ArgumentList.Add(a);using var p=System.Diagnostics.Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await output;if(p.ExitCode!=0)throw new IOException(await errors);}
        await Git("init");File.WriteAllText(Path.Combine(repo,"Tracked.cs"),"COMMITTED_VERSION");File.WriteAllText(Path.Combine(repo,".gitignore"),"private.fixture\n");await Git("add","Tracked.cs",".gitignore");await Git("-c","user.name=Infinity fixture","-c","user.email=fixture@localhost","-c","core.hooksPath=NUL","commit","--no-gpg-sign","-m","Fixture base");
        File.WriteAllText(Path.Combine(repo,"Tracked.cs"),"USER_DIRTY_VERSION");File.WriteAllText(Path.Combine(repo,"private.fixture"),"SYNTHETIC_PRIVATE_CONFIG");File.WriteAllText(Path.Combine(repo,"Untracked.cs"),"USER_UNTRACKED_VERSION");
        var original=CreateChatProject("Original isolated fixture",repo,"");prompt.Text="ORIGINAL_WORKSPACE_DRAFT";var originalChat=conversation;
        var plan=await IsolatedWorkspace.Preview(repo,"HEAD",Path.Combine(root,"managed"));
        rows["worktreePreviewResolvesExactCommitWithoutCheckingOut"]=plan.Commit.Length==40&&!Directory.Exists(plan.Destination)&&File.ReadAllText(Path.Combine(repo,"Tracked.cs"))=="USER_DIRTY_VERSION";
        await IsolatedWorkspace.Create(plan);var isolated=RegisterIsolatedProject(original,plan);
        rows["isolatedWorkspaceUsesCommittedFilesAndExcludesPrivateUntrackedData"]=File.ReadAllText(Path.Combine(plan.Destination,"Tracked.cs"))=="COMMITTED_VERSION"&&!File.Exists(Path.Combine(plan.Destination,"private.fixture"))&&!File.Exists(Path.Combine(plan.Destination,"Untracked.cs"));
        File.WriteAllText(Path.Combine(plan.Destination,"Tracked.cs"),"ISOLATED_CHANGE");rows["isolatedEditsDoNotChangeOriginalWorkingFiles"]=File.ReadAllText(Path.Combine(repo,"Tracked.cs"))=="USER_DIRTY_VERSION"&&File.ReadAllText(Path.Combine(repo,"Untracked.cs"))=="USER_UNTRACKED_VERSION";
        rows["isolatedProjectAndChatPersistExactWorkspaceScope"]=conversation.ProjectId==isolated.Id&&conversation.WorkspaceRoots.SequenceEqual([plan.Destination])&&new AgentStore(AgentDirectory).Load().Projects.Single(p=>p.Id==isolated.Id).IsolationCommit==plan.Commit;
        SelectProjectChats(original.Id);rows["returningFromIsolationRestoresOriginalDraftAndScope"]=conversation==originalChat&&prompt.Text=="ORIGINAL_WORKSPACE_DRAFT"&&conversation.WorkspaceRoots.SequenceEqual([repo]);
        var rejected=false;try{await IsolatedWorkspace.Preview(repo,"--output=unsafe",Path.Combine(root,"managed"));}catch(IOException){rejected=true;}rows["isolatedWorkspaceRejectsRevisionOptions"]=rejected;
        async Task ExerciseDialog(bool approve)
        {
            var previewClicked=false;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};timer.Tick+=(_,_)=>
            {
                var choose=Application.Current.Windows.OfType<WorkspaceIsolationWindow>().FirstOrDefault();
                if(choose!=null&&!previewClicked){previewClicked=true;Descendants<Button>(choose).Single(b=>Equals(b.Content,"Preview")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));return;}
                var review=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title=="Create isolated workspace");
                if(review==null)return;
                rows["workspaceNativeReviewShowsExactCommitDestinationAndAuthority"]=Descendants<TextBox>(review).Single().Text.Contains("Exact commit: "+plan.Commit)&&Descendants<TextBox>(review).Single().Text.Contains("not an operating-system sandbox");
                if(approve)Descendants<Button>(review).Single(b=>Equals(b.Content,"Approve this exact action")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));else review.Close();timer.Stop();
            };
            timer.Start();try{await CreateIsolatedWorkspace();}finally{timer.Stop();}
        }
        var projectCount=Agent.Projects.Count;await ExerciseDialog(false);rows["cancelNativeWorkspaceReviewCreatesNoProjectOrWorktree"]=Agent.Projects.Count==projectCount&&conversation==originalChat;
        await ExerciseDialog(true);rows["nativeWorkspaceReviewCreatesAndOpensSeparateProject"]=Agent.Projects.Count==projectCount+1&&conversation.ProjectId!=original.Id&&File.Exists(Path.Combine(conversation.WorkspaceRoots.Single(),"Tracked.cs"))&&ProjectForChat()!.IsolationCommit==plan.Commit;
        UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".capabilities.png"));
    }
}
