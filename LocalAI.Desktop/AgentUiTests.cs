using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal static class AgentUiTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"agent-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);MainWindow? window=null;
        try
        {
            var profile=new ModelProfile{Id="ui-fixture",Label="Isolated UI fixture",Engine="ryzen",Endpoint="http://127.0.0.1:62341",ModelPath=root};
            window=new MainWindow(["--ui-session",Path.Combine(root,"session.json")],new Settings{Models=[profile]});window.Show();await window.RunAgentUiTests(rows,root,profile);window.Close();window=null;var saved=JsonSerializer.Deserialize<AgentState>(File.ReadAllText(Path.Combine(root,"agent","agent.json")))!;rows["mainWindowCloseSavesEmbeddedAgentDraft"]=saved.Draft=="MAIN_CLOSE_AGENT_DRAFT"&&saved.Paused&&!saved.Tasks.Any(t=>t.Status is "Running" or "Queued");
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Values.All(v=>v is true)?0:1);
    }
}
public sealed partial class MainWindow
{
    internal async Task RunAgentUiTests(Dictionary<string,object> rows,string root,ModelProfile profile)
    {
        static bool Refused(Action action){try{action();return false;}catch(IOException){return true;}}
        if(inspectorColumn.Width.Value==0)ToggleInspector();
        var nativeWindows=Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible);OpenAgent();
        rows["agentIsEmbeddedInMainWindow"]=Window.GetWindow(agentView)==this&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==nativeWindows;
        var originalView=agentView;OpenAgent();rows["repeatedAgentNavigationReusesOneView"]=ReferenceEquals(originalView,agentView)&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==nativeWindows;
        for(var i=0;i<80;i++)Agent.Messages.Add(new("user","Earlier history fixture line "+i));CloseAgentView();OpenAgent();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        rows["embeddedAgentStartsAtLatestConversationMessage"]=agentView!.TestTranscriptAtEnd;
        var routeRequest=new AgentTaskRequest{Title="Existing task route",Instructions="Preserved fixture; do not start it.",ProjectRoot=root,ModelId=profile.Id};var routeTask=SubmitAgentTask(routeRequest);var routeCount=Agent.Tasks.Count;var requestPath=Path.Combine(root,"existing-request.json");File.WriteAllText(requestPath,JsonSerializer.Serialize(routeRequest));await StartSubmittedTask(requestPath);
        rows["existingTaskRequestOpensEmbeddedAgentWithoutReplay"]=AgentSurfaceActive&&Window.GetWindow(agentView)==this&&routeTask.Status=="Paused"&&!AgentIsWorking&&Agent.Tasks.Count==routeCount&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==nativeWindows;
        prompt.Text="MAIN_CHAT_DRAFT";agentView!.TestDraft("AGENT_DRAFT");var tasksBefore=Agent.Tasks.Count;ShowChat();ShowCode();OpenAgent();
        rows["switchingChatCodeAgentPreservesSeparateDrafts"]=prompt.Text=="MAIN_CHAT_DRAFT"&&Agent.Draft=="AGENT_DRAFT"&&agentView.TestComposer=="AGENT_DRAFT";
        rows["switchingViewsDoesNotStartOrDuplicateTasks"]=Agent.Tasks.Count==tasksBefore&&!AgentIsWorking&&Agent.Paused;
        rows["agentDraftSavedOnNavigation"]=new AgentStore(AgentDirectory).Load().Draft=="AGENT_DRAFT";
        OpenCode("let original = 1","swift",null,false);codeEditor.Text="let edited = 2";OpenAgent();ShowCode();
        rows["agentNavigationPreservesCodeEditorDraft"]=codeEditor.Text=="let edited = 2";ShowChat();UpdateLayout();
        rows["chatInspectorReturnsAfterAgentNavigation"]=inspectorContent.IsVisible&&chatView.IsVisible&&!agentSurface.IsVisible;OpenAgent();
        agentView!.TestDraft("");
        rows["generalWorkspaceIsExplicit"]=agentView!.TestProject=="General workspace";
        rows["pausedStatusDoesNotClaimWorkIsRunning"]=agentView.TestActivity.Contains("Paused")&&!AgentIsWorking;
        var folder=Path.Combine(root,"project");Directory.CreateDirectory(folder);var other=Path.Combine(root,"other");Directory.CreateDirectory(other);
        var project=new ProjectMemory{Name="Acme iPad",Roots=[folder],Purpose="PROJECT_ONE_MARKER",BuildCommand="build-fixture",TestCommand="test-fixture"};SaveProject(project);
        rows["activeProjectVisibleAndPersisted"]=agentView.TestProject==project.Name&&new AgentStore(AgentDirectory).Load().ActiveProjectId==project.Id;
        var chat=new Conversation{Title="Existing Acme task",ModelId=profile.Id,ProjectId=project.Id,Messages=[new("user","Original task")]};store.Save(chat);conversations.Add(chat);
        var job=new AgentTask{Title="Existing Acme task",Instructions="Original task",ProjectId=project.Id,ChatId=chat.Id,ModelId=profile.Id,Status="Response ready",Messages=[new("user","Original task")],VerifiedAt=DateTimeOffset.UtcNow};Agent.Tasks.Add(job);
        var second=new ProjectMemory{Name="Other project",Roots=[other],Purpose="PROJECT_TWO_MARKER"};SaveProject(second);SaveAgent();agentView.TestDraft("Continue implementing Acme");agentView.TestChooseDestination(job.Id);
        rows["followUpDestinationNamesExistingTask"]=agentView.TestDestination.Contains(job.Title);
        rows["followUpShowsTaskProjectDespiteDifferentActiveProject"]=agentView.TestProject==second.Name&&agentView.TestActivity.Contains(project.Name);
        var saved=new AgentStore(AgentDirectory).Load();rows["destinationAndDraftPersistTogether"]=saved.DraftTaskId==job.Id&&saved.Draft=="Continue implementing Acme";
        Agent.Paused=false;QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="write",path=Path.Combine(folder,"Pending.swift"),content="import Foundation"}}),job);var pending=Agent.Approvals.Last();var before=job.Messages.Count;
        AgentFollowUp(job,"Must not run");rows["followUpCannotBypassPendingReview"]=job.FollowUps.Last().Status=="Pending"&&job.Messages.Count==before;
        rows["queuedReviewFollowUpPersistsWithoutSending"]=new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==job.Id).FollowUps.Single().Text=="Must not run";job.FollowUps.Clear();Agent.Draft="Continue implementing Acme";SaveAgent();
        rows["approvalNamesFileAndOperation"]=AgentPresentation.ApprovalLabel(pending).Contains("Pending.swift")&&AgentPresentation.ApprovalLabel(pending).Contains("write");
        rows["approvalInboxNamesExactFile"]=Agent.Inbox.Last().Text.Contains("Pending.swift");
        OpenAgentWorkbench("Changes");agentWorkbench!.UpdateLayout();rows["agentProjectToolsShareMainWindow"]=Window.GetWindow(agentWorkbench)==this&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==nativeWindows;rows["changesWorkspaceRendersNamedApproval"]=RenderedText(agentWorkbench).Any(t=>t.Text.Contains("Pending.swift")&&t.Text.Contains("Needs review"));QaCapture.Save(agentWorkbench,Path.Combine(root,"named-changes.png"));
        OpenAgentWorkbench("Verification");agentWorkbench.UpdateLayout();rows["verificationWorkspaceDistinguishesReplyFromCompletion"]=RenderedText(agentWorkbench).Any(t=>t.Text.Contains("completion unverified"));CloseAgentWorkbench();
        rows["waitingReviewStatusIsExplicit"]=agentView.TestActivity.Contains("Waiting for review");await AgentReview(pending,true);
        rows["unknownTaskFollowUpRefused"]=Refused(()=>AgentFollowUp(new AgentTask(),"No action"));
        rows["oversizeFollowUpRefused"]=Refused(()=>AgentFollowUp(job,new string('x',16001)));
        before=job.Messages.Count;job.Status="Running";AgentFollowUp(job,"No action");rows["runningTaskFollowUpQueuesWithoutReplaying"]=job.FollowUps.Single().Status=="Pending"&&job.Messages.Count==before;job.FollowUps.Clear();SaveAgent();job.Status="Response ready";
        var old=DateTimeOffset.UtcNow.AddMinutes(-2);job.Evidence.Add(new(Guid.NewGuid(),"build",project.BuildCommand,folder,"Succeeded",0,"old fixture","",AgentWorkspaceFeatures.Snapshot(project),old));job.Evidence.Add(new(Guid.NewGuid(),"test",project.TestCommand,folder,"Succeeded",0,"old fixture","",AgentWorkspaceFeatures.Snapshot(project),old));
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var shutdown=new CancellationTokenSource();profile.Endpoint=$"http://127.0.0.1:{port}";string? packet=null;
        var serving=Task.Run(async()=>{while(!shutdown.IsCancellationRequested){HttpListenerContext c;try{c=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch(Exception e) when(e is OperationCanceledException or ObjectDisposedException or HttpListenerException){break;}try{string result;if(c.Request.Url!.AbsolutePath=="/health")result=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});else{using var reader=new StreamReader(c.Request.InputStream);packet=await reader.ReadToEndAsync();var answer=JsonSerializer.Serialize(new{action="reply",value="FOLLOWUP_VERIFIED_REPLY"});result="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=answer},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(result));c.Response.Close();}catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){try{c.Response.Abort();}catch(ObjectDisposedException){}}}});
        var ownsGate=false;
        try
        {
            await modelGate.WaitAsync();ownsGate=true;var count=Agent.Tasks.Count;await agentView.TestSend("Continue implementing Acme",profile.Id);
            rows["followUpReusesTaskChatAndModel"]=Agent.Tasks.Count==count&&job.ChatId==chat.Id&&job.ModelId==profile.Id&&job.Messages.Last().Content=="Continue implementing Acme"&&store.Load(out _).Single(c=>c.Id==chat.Id).Messages.Last().Content=="Continue implementing Acme";
            rows["workingStatusReflectsActualRequest"]=AgentIsWorking&&agentView.TestActivity.Contains("Working");
            rows["followUpInvalidatesVerifiedBadgeButRetainsAuditEvidence"]=job.VerifiedAt==null&&job.Evidence.Count==2&&job.VerificationSince.HasValue;
            rows["oldChecksCannotVerifyNewFollowUp"]=AgentWorkspaceFeatures.Verification(job,project,AgentWorkspaceFeatures.Snapshot(project),false)!="Verified";
            QaCapture.Save(agentView,Path.Combine(root,"agent-working.png"));modelGate.Release();ownsGate=false;
            for(var n=0;n<500&&taskRequest!=null;n++)await Task.Delay(10);
            rows["followUpRunsAndReceivesReplyInSameTask"]=taskRequest==null&&job.Status=="Response ready"&&chat.Messages.Last().Content=="FOLLOWUP_VERIFIED_REPLY";
            rows["followUpUsesOriginalProjectContext"]=packet?.Contains("PROJECT_ONE_MARKER")==true&&!packet.Contains("PROJECT_TWO_MARKER");
            rows["followUpAppearsInTaskTranscript"]=agentView.TestTranscript.Contains("Continue implementing Acme")&&agentView.TestTranscript.Contains("FOLLOWUP_VERIFIED_REPLY");
            rows["replyReceivedIsNotPresentedAsCompletion"]=AgentPresentation.TaskLabel(job).Contains("completion unverified");
            rows["followUpDoesNotDuplicateIntoGeneralUserHistory"]=!Agent.Messages.Any(m=>m.Role=="user"&&m.Content=="Continue implementing Acme");
            agentView.TestChooseDestination(null);rows["canReturnToOngoingAgentWithoutCreatingTask"]=Agent.DraftTaskId==null&&Agent.Tasks.Count==count;
            Agent.Paused=true;SaveAgent();await modelGate.WaitAsync();ownsGate=true;var general=agentView.TestSend("General reply while background tasks are paused",profile.Id);
            rows["pausedBackgroundQueueDoesNotMislabelNewReplyAsStopping"]=AgentIsWorking&&agentView.TestActivity.Contains("Agent is working")&&!agentView.TestActivity.Contains("Stopping");AgentStopReply();await general;modelGate.Release();ownsGate=false;
        }
        finally{AgentPause();if(ownsGate)modelGate.Release();shutdown.Cancel();listener.Stop();await serving;}
        agentView!.TestDraft("Pending task continuation");agentView.TestChooseDestination(job.Id);CloseAgentView();OpenAgent();
        rows["taskDestinationAndPendingPromptSurviveViewReopen"]=agentView!.TestDestination.Contains(job.Title)&&Agent.Draft=="Pending task continuation";agentView.TestChooseDestination(null);
        SelectAgentProject(project.Id);
        var first=Path.Combine(folder,"SwiftUI");var secondFolder=Path.Combine(folder,"Transport");
        QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="mkdir",path=first},new{operation="mkdir",path=secondFolder}}),null);var group=Agent.Approvals.Last();
        rows["folderApprovalNamesRelatedPaths"]=AgentPresentation.ApprovalLabel(group).Contains("2 folders")&&AgentPresentation.ApprovalLabel(group).Contains("SwiftUI")&&AgentPresentation.ApprovalLabel(group).Contains("Transport");
        agentFolderReviewTest=async review=>{rows["agentFolderReviewOwnedByMainWindow"]=review.Owner==this;await review.TestPreview();rows["groupPreviewHasNoFilesystemEffects"]=review.HasPreview&&!Directory.Exists(first)&&!Directory.Exists(secondFolder);QaCapture.Save(review,Path.Combine(root,"folder-group.png"));await review.TestApply();if(review.Receipts.Count==0)review.Close();};await AgentReview(group);
        rows["oneGroupReviewCreatesBothExactFolders"]=Directory.Exists(first)&&Directory.Exists(secondFolder)&&group.Status.StartsWith("Review finished");
        rows["groupRecordsEveryFolderReceiptAndJournalEntry"]=Agent.Changes.Count(c=>c.ApprovalId==group.Id&&c.Status=="Applied"&&c.Receipt?.Operation=="mkdir")==2;
        rows["handledGroupCannotReplay"]=await RefusedAsync(()=>AgentReview(group));
        var cancelledA=Path.Combine(folder,"CancelledA");var cancelledB=Path.Combine(folder,"CancelledB");QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="mkdir",path=cancelledA},new{operation="mkdir",path=cancelledB}}),null);var cancelled=Agent.Approvals.Last();agentFolderReviewTest=async review=>{await review.TestPreview();review.Close();};await AgentReview(cancelled);
        rows["cancelledGroupCreatesNothing"]=!Directory.Exists(cancelledA)&&!Directory.Exists(cancelledB)&&cancelled.Status.StartsWith("Cancelled");
        var partialA=Path.Combine(folder,"PartialA");var partialB=Path.Combine(folder,"PartialB");QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="mkdir",path=partialA},new{operation="mkdir",path=partialB}}),null);var partial=Agent.Approvals.Last();agentFolderReviewTest=async review=>{await review.TestPreview();Directory.CreateDirectory(partialB);await review.TestApply();rows["staleGroupStopsWithPartialReceipts"]=review.Receipts.Count==1&&review.Failure!=null;review.Close();};await AgentReview(partial);
        rows["partialGroupDoesNotClaimFullSuccess"]=partial.Status.StartsWith("Needs attention")&&Agent.Changes.Count(c=>c.ApprovalId==partial.Id&&c.Status=="Applied")==1;
        rows["failedOutcomesStayVisibleForInspection"]=agentView!.TestHasApproval(partial.Id);
        var write=Path.Combine(folder,"ordinary.txt");QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="write",path=write,content="Reviewed individually"}}),null);var ordinary=Agent.Approvals.Last();var singleUsed=false;agentFileReviewTest=async review=>{singleUsed=true;rows["agentFileReviewOwnedByMainWindow"]=review.Owner==this;await review.TestPreview();await review.TestApply();if(review.Receipt==null)review.Close();};await AgentReview(ordinary);
        rows["fileWritesKeepIndividualExactReview"]=singleUsed&&File.ReadAllText(write)=="Reviewed individually";
        var mixedRefused=false;try{_=new FolderBatchReviewWindow(new FileWorkspace([folder]),[new("mkdir",Path.Combine(folder,"Never")),new("write",write,Content:"No")]);}catch(IOException){mixedRefused=true;}rows["folderGroupRejectsMixedWriteActions"]=mixedRefused;
        var parent=Path.Combine(folder,"NestedParent");var child=Path.Combine(parent,"Child");QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="mkdir",path=parent},new{operation="mkdir",path=child}}),null);var nested=Agent.Approvals.Last();
        agentFolderReviewTest=async review=>{await review.TestPreview();rows["dependentFoldersRequireSequentialReview"]=!review.HasPreview&&!Directory.Exists(parent);Descendants<System.Windows.Controls.Button>(review).Single(b=>b.Content?.ToString()=="Review one at a time").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));};await AgentReview(nested);
        rows["individualFallbackHandlesParentThenChild"]=Directory.Exists(parent)&&Directory.Exists(child)&&nested.Status.StartsWith("Review finished");
        var outside=Path.Combine(root,"OutsideScope");var invalid=new FolderBatchReviewWindow(new FileWorkspace([folder]),[new("mkdir",Path.Combine(folder,"AllowedButNotApplied")),new("mkdir",outside)]){Owner=this};invalid.Show();await invalid.TestPreview();await invalid.TestApply();rows["groupCannotEscapeConnectedScope"]=!invalid.HasPreview&&!Directory.Exists(outside)&&invalid.Receipts.Count==0;invalid.Close();
        var duplicatePath=Path.Combine(folder,"Duplicate");var duplicate=new FolderBatchReviewWindow(new FileWorkspace([folder]),[new("mkdir",duplicatePath),new("mkdir",duplicatePath)]){Owner=this};duplicate.Show();await duplicate.TestPreview();rows["duplicateFolderTargetsRefusedBeforeApply"]=!duplicate.HasPreview&&!Directory.Exists(duplicatePath);duplicate.Close();
        agentFolderReviewTest=null;agentFileReviewTest=null;QueueApproval("files",JsonSerializer.Serialize(new[]{new{operation="mkdir",path=Path.Combine(folder,"PreviewOnlyA")},new{operation="mkdir",path=Path.Combine(folder,"PreviewOnlyB")}}),null);agentView!.Refresh();DarkTheme.ConstrainToWorkArea(this,new Rect(0,0,1366,728));UpdateLayout();QaCapture.Save(agentView,Path.Combine(root,"agent-1366x768.png"));QaCapture.Save(this,Path.Combine(root,"single-window-1366x768.png"));
        static IEnumerable<System.Windows.Controls.TextBlock> RenderedText(DependencyObject node){for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(node,i);if(child is System.Windows.Controls.TextBlock text)yield return text;foreach(var nestedText in RenderedText(child))yield return nestedText;}}
        rows["approvalPathLabelsActuallyRender"]=RenderedText(agentView).Any(t=>t.Text.Contains("PreviewOnlyA")&&t.Text.Contains("Needs review"));
        rows["onlyMainWindowRemainsAfterAgentWorkflow"]=Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==nativeWindows;
        rows["agentControlsFit1366x768WorkArea"]=agentView.ActualHeight<=728&&agentView.ActualWidth<=1366&&Descendants<System.Windows.Controls.ComboBox>(agentView).Any(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Agent message destination");
        rows["taskAndApprovalPanesRemainReadableAt1366x768"]=agentView.TestPanesReadable;
        var trustedToggle=Descendants<System.Windows.Controls.CheckBox>(agentView).Single(c=>c.Content?.ToString()=="Let the AI work automatically");trustedToggle.IsChecked=true;rows["trustedWorkControlTogglesAndPersists"]=Agent.TrustedWork;trustedToggle.IsChecked=false;
        CloseAgentView();OpenAgent();rows["enhancedAgentReopensSameWorkHistory"]=Agent.Tasks.Any(t=>t.Id==job.Id)&&Agent.Approvals.Any(a=>a.Id==group.Id)&&agentView!.TestProject==project.Name;AgentPause();agentView!.TestDraft("Original task");AgentSaveDraft("Original task");CloseAgentView();
        var competitor=new AgentStore(AgentDirectory);var competingState=competitor.Load();competingState.Memory="EXTERNAL_STATE_MUST_SURVIVE";competitor.Save(competingState);var messageCount=job.Messages.Count;var chatCount=chat.Messages.Count;
        rows["staleAgentSaveRefusesFollowUp"]=Refused(()=>AgentFollowUp(job,"Original task"));
        rows["failedFollowUpPreservesIdenticalEarlierMessages"]=job.Messages.Count==messageCount&&chat.Messages.Count==chatCount&&job.Messages.First().Content=="Original task"&&store.Load(out _).Single(c=>c.Id==chat.Id).Messages.Count==chatCount;
        rows["failedFollowUpPreservesDraftAndExternalState"]=Agent.Draft=="Original task"&&new AgentStore(AgentDirectory).Load().Memory=="EXTERNAL_STATE_MUST_SURVIVE";
        agentStore=new(AgentDirectory);agentState=agentStore.Load();OpenAgent();agentView!.TestDraft("MAIN_CLOSE_AGENT_DRAFT");
    }
    private static async Task<bool> RefusedAsync(Func<Task> action){try{await action();return false;}catch(IOException){return true;}}
}
