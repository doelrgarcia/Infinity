using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal static class UiAgentPathTests
{
    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var rows=new Dictionary<string,object>();MainWindow? window=null;
        try{window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"agent-path-session","session.json")]);window.Show();await window.ExerciseAgentUiPaths(rows,Path.GetDirectoryName(output)!);QaCapture.Save(window,Path.ChangeExtension(output,".png"));}
        catch(Exception error){rows["error"]=error.ToString();}finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==116&&rows.Values.All(v=>v is true)?0:1);
    }
}

public sealed partial class MainWindow
{
    internal async Task ExerciseAgentUiPaths(Dictionary<string,object> rows,string output)
    {
        void Check(string name,bool value){rows[name]=value;if(!value)throw new IOException("Agent UI assertion failed: "+name);}
        Button Find(DependencyObject area,string label)=>UiDialogPathTests.Find<Button>(area).Single(b=>Equals(b.Content,label)||AutomationProperties.GetName(b)==label);
        TextBox Field(DependencyObject area,string name)=>UiDialogPathTests.Find<TextBox>(area).Single(t=>AutomationProperties.GetName(t)==name);
        async Task Click(DependencyObject area,string label){for(var n=0;n<300&&agentWorkbench?.IsWorking==true;n++)await Task.Delay(20);UiPathTests.Click(Find(area,label));for(var n=0;n<300&&agentWorkbench?.IsWorking==true;n++)await Task.Delay(20);Check("agentButtonFinishes_"+label,agentWorkbench?.IsWorking!=true);UpdateLayout();}
        async Task Modal(string key,Action action,Action<Window> inspect)
        {
            Window? opened=null;Exception? error=null;var finished=new TaskCompletionSource();
            _=Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    for(var n=0;n<300&&opened==null;n++){opened=Application.Current.Windows.OfType<Window>().SingleOrDefault(w=>w.Owner==this&&w.IsVisible);if(opened==null)await Task.Delay(20);}
                    if(opened!=null)inspect(opened);
                }
                catch(Exception e){error=e;}
                finally{if(opened?.IsVisible==true)opened.Close();finished.TrySetResult();}
            });
            action();await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));if(error!=null)throw error;for(var n=0;n<300&&agentWorkbench?.IsWorking==true;n++)await Task.Delay(20);Check(key,opened!=null&&!opened.IsVisible);
        }
        var root=Path.Combine(output,"agent-path-fixture");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"Example.cs"),"class UI_AGENT_SEARCH_MARKER {}");
        UiPathTests.Click(agentEntry!);Check("tasksSidebarButtonOpensAgentSurface",AgentSurfaceActive&&agentView!=null);
        var view=agentView!;
        var mic=Find(view,"Dictate Agent prompt");var fakeVoice=new TestDictation();view.VoiceSessionFactoryTest=()=>fakeVoice;
        try
        {
            var priorMessages=Agent.Messages.Count;Field(view,"Message Infinity Agent").Text="Synthetic voice draft";UiPathTests.Click(mic);
            Check("agentMicrophoneButtonStartsSession",fakeVoice.Started&&Equals(mic.Content,"■ Stop recording"));
            fakeVoice.Say("recognized words");Check("agentDictationAppendsWithoutSending",view.TestComposer=="Synthetic voice draft recognized words"&&Agent.Messages.Count==priorMessages);
            UiPathTests.Click(mic);Check("agentMicrophoneStopRestoresReadyAfterSynchronousCompletion",fakeVoice.Disposed&&Equals(mic.Content,"● Speak"));
            fakeVoice=new(){FailStart=true};UiPathTests.Click(mic);Check("agentMicrophoneFailureRetainsDraftAndAllowsRetry",fakeVoice.Disposed&&Equals(mic.Content,"● Speak")&&view.TestComposer=="Synthetic voice draft recognized words"&&UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text.Contains("Synthetic microphone unavailable")));
            fakeVoice=new(){CompleteStart=true};UiPathTests.Click(mic);Check("agentImmediateStartCompletionKeepsReadyStateAndError",fakeVoice.Disposed&&Equals(mic.Content,"● Speak")&&UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text=="Synthetic immediate start failure"));
            Field(view,"Message Infinity Agent").Clear();
        }
        finally{view.StopVoice();view.VoiceSessionFactoryTest=null;}
        await Click(view,"Pause work");Check("pauseWorkButtonPersistsPausedState",new AgentStore(AgentDirectory).Load().Paused);
        await Click(view,"Stop reply");Check("stopIdleAgentReplyLeavesSurfaceUsable",AgentSurfaceActive);
        await Modal("agentMemoryReviewRoute",()=>UiPathTests.Click(Find(view,"Memory")),dialog=>
        {
            var text=UiDialogPathTests.Find<TextBox>(dialog).Single();text.Text="Synthetic persistent Agent memory";UiPathTests.Click(Find(dialog,"Save reviewed text"));
        });
        Check("agentMemorySaveButtonPersistsReviewedText",Agent.Memory=="Synthetic persistent Agent memory"&&new AgentStore(AgentDirectory).Load().Memory==Agent.Memory);
        await Modal("agentFoldersReviewRoute",()=>UiPathTests.Click(Find(view,"Connected folders")),dialog=>
        {
            UiDialogPathTests.Find<TextBox>(dialog).Single().Text=root;UiPathTests.Click(Find(dialog,"Save reviewed text"));
        });
        Check("agentFoldersSaveButtonPersistsExactFolder",Agent.Roots.SequenceEqual(new[]{root})&&new AgentStore(AgentDirectory).Load().Roots.SequenceEqual(new[]{root}));
        await Click(view,"Follow up selected task");Check("followUpWithoutSelectionShowsActionableError",UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text.Contains("Select a task")));
        await Click(view,"Review selected proposal");Check("reviewWithoutSelectionShowsActionableError",UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text.Contains("Select a proposal")));
        var handled=UiDialogPathTests.Find<CheckBox>(view).Single(c=>Equals(c.Content,"Show handled"));UiPathTests.Click(handled);Check("showHandledCheckboxTogglesOn",handled.IsChecked==true);UiPathTests.Click(handled);Check("showHandledCheckboxTogglesOff",handled.IsChecked==false);
        UiPathTests.Click(UiDialogPathTests.Find<Button>(view).Single(b=>b.Content is string label&&label.StartsWith("Workspace & inbox")));
        Check("workspaceInboxButtonOpensProjectWorkbench",agentWorkbench?.TestView=="Projects"&&agentSurface.Content==agentWorkbench);
        await Click(agentWorkbench!,"Agent conversation");
        await Click(view,"Manage project");Check("manageProjectButtonOpensWorkbench",agentWorkbench?.TestView=="Projects"&&agentSurface.Content==agentWorkbench);
        var workbench=agentWorkbench!;
        Field(workbench,"Project name").Text="Agent path project";Field(workbench,"Connected folders — one full path per line").Text=root;Field(workbench,"Purpose").Text="Synthetic purpose";
        await Click(workbench,"Save & use project");var project=Agent.Projects.Single(p=>p.Name=="Agent path project");Check("workbenchProjectSavePersistsFields",Agent.ActiveProjectId==project.Id&&new AgentStore(AgentDirectory).Load().Projects.Single(p=>p.Id==project.Id).Purpose=="Synthetic purpose");
        await Modal("workbenchAttachProjectConfirmation",()=>UiPathTests.Click(Find(workbench,"Use project for current coding chat")),dialog=>UiPathTests.Click(Find(dialog,"Approve this exact action")));
        Check("workbenchAttachButtonUpdatesCodingChatScope",conversation.ProjectId==project.Id&&conversation.WorkspaceRoots.SequenceEqual(new[]{root}));
        await Click(workbench,"Use general workspace");Check("generalWorkspaceButtonRetainsSavedProject",Agent.ActiveProjectId==null&&Agent.Projects.Any(p=>p.Id==project.Id));
        await Click(workbench,"New project");Check("workbenchNewProjectButtonClearsDraft",Field(workbench,"Project name").Text.Length==0&&Field(workbench,"Purpose").Text.Length==0);
        foreach(var section in new[]{"Changes","Verification","Search","Inbox","Routing","Projects"})
        {await Click(workbench,section=="Search"?"Open Agent Search workspace":section);Check("workbenchNavigation_"+section,workbench.TestView==section&&AgentSurfaceActive);}
        await Click(workbench,"Changes");await Click(workbench,"Review & apply proposal");Check("changesReviewWithoutSelectionShowsError",UiDialogPathTests.Find<TextBlock>(workbench).Any(t=>t.Text.Contains("Select a pending")));
        await Click(workbench,"Queue rollback");Check("rollbackWithoutSelectionShowsError",UiDialogPathTests.Find<TextBlock>(workbench).Any(t=>t.Text.Contains("Select an applied")));
        project.BuildCommand="[Console]::WriteLine('UI_BUILD_RECEIPT')";project.TestCommand="[Console]::WriteLine('UI_TEST_RECEIPT')";SaveProject(project,false);
        var job=new AgentTask{Title="UI verification fixture",Instructions="Verify only synthetic command receipts",ModelId=settings.Models[0].Id,ProjectId=project.Id,Status="Paused"};Agent.Tasks.Add(job);SaveAgent();
        Exception? commandFailure=null;
        agentCommandReviewTest=async dialog=>
        {
            try
            {
                UiPathTests.Click(Find(dialog,"Review exact command"));UiPathTests.Click(UiDialogPathTests.Find<CheckBox>(dialog).Single());UiPathTests.Click(Find(dialog,"Run reviewed command"));
                for(var n=0;n<500&&Find(dialog,"Stop run").IsEnabled;n++)await Task.Delay(20);
                if(dialog.Result?.Status!="Succeeded")throw new IOException("Synthetic verification command failed.");dialog.Close();
            }
            catch(Exception error){commandFailure=error;dialog.CancelForOwner();dialog.Close();}
        };
        foreach(var (label,kind,marker) in new[]{("Review project build","build","UI_BUILD_RECEIPT"),("Review project tests","test","UI_TEST_RECEIPT")})
        {
            await Click(workbench,"Verification");UiDialogPathTests.Find<ListBox>(workbench).Single().SelectedItem=job;await Click(workbench,label);
            if(commandFailure!=null)throw commandFailure;
            Check("verificationButtonRecordsActualCommand_"+kind,job.Evidence.Any(e=>e.Kind==kind&&e.ExitCode==0&&e.Output.Contains(marker)&&File.Exists(e.LogPath)));
        }
        agentCommandReviewTest=null;
        UiDialogPathTests.Find<ListBox>(workbench).Single().SelectedItem=job;
        await Modal("verificationButtonOpensResultReview",()=>UiPathTests.Click(Find(workbench,"Verify completion")),dialog=>
        {Check("verificationReviewShowsActualReceiptVerdict",dialog.Title=="Task verification"&&UiDialogPathTests.Find<TextBox>(dialog).Single().Text=="Verified");UiPathTests.Click(Find(dialog,"Close"));});
        Check("verificationButtonPersistsReceiptBasedState",job.VerifiedAt!=null&&new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==job.Id).Status=="Verified complete");
        await Click(workbench,"Inbox");var inbox=UiDialogPathTests.Find<ListBox>(workbench).Single();var update=Agent.Inbox.First(n=>n.TaskId==job.Id&&n.ApprovalId!=null);inbox.SelectedItem=update;
        await Click(workbench,"Mark read");Check("inboxMarkReadButtonPersistsSelectedNotice",new AgentStore(AgentDirectory).Load().Inbox.Single(n=>n.Id==update.Id).Read);
        UiDialogPathTests.Find<ListBox>(workbench).Single().SelectedItem=update;
        await Modal("inboxTaskEvidenceButtonOpensReview",()=>UiPathTests.Click(Find(workbench,"Task evidence")),dialog=>
        {Check("inboxTaskEvidenceShowsActualCommandOutput",UiDialogPathTests.Find<TextBox>(dialog).Single().Text.Contains("UI_TEST_RECEIPT"));UiPathTests.Click(Find(dialog,"Close"));});
        UiDialogPathTests.Find<ListBox>(workbench).Single().SelectedItem=update;await Click(workbench,"Review linked approval");Check("handledInboxApprovalCannotBeAppliedAgain",UiDialogPathTests.Find<TextBlock>(workbench).Any(t=>t.Text.Contains("already been handled")));
        UiDialogPathTests.Find<ListBox>(workbench).Single().SelectedIndex=-1;await Click(workbench,"Review stronger help");Check("strongerInboxButtonRequiresARequest",UiDialogPathTests.Find<TextBlock>(workbench).Any(t=>t.Text.Contains("composer first")));
        await Click(workbench,"Open Agent Search workspace");var query=UiDialogPathTests.Find<TextBox>(workbench).Single();query.Text="UI_AGENT_SEARCH_MARKER";await Click(workbench,"Search connected chats and files");
        var results=UiDialogPathTests.Find<ListBox>(workbench).Single();Check("workbenchSearchButtonFindsActualSource",results.Items.OfType<WorkspaceSearchHit>().Any(r=>r.Excerpt.Contains("UI_AGENT_SEARCH_MARKER")));
        await Click(workbench,"Stop search");Check("stopSearchButtonLeavesResultsAvailable",results.Items.Count>0);
        results.SelectedIndex=0;await Modal("openSearchResultButtonOpensReview",()=>UiPathTests.Click(Find(workbench,"Open result")),dialog=>
        {Check("openSearchResultButtonOpensActualFile",dialog.Title.StartsWith("Search result:")&&UiDialogPathTests.Find<TextBox>(dialog).Single().Text.Contains("UI_AGENT_SEARCH_MARKER"));UiPathTests.Click(Find(dialog,"Close"));});
        OpenAgentWorkbench("Routing");var local=UiDialogPathTests.Find<CheckBox>(workbench).Single(c=>Equals(c.Content,"Local first"));var offer=UiDialogPathTests.Find<CheckBox>(workbench).Single(c=>Equals(c.Content,"Offer stronger-model help when local generation fails"));UiPathTests.Click(local);UiPathTests.Click(offer);await Click(workbench,"Save routing preferences");var saved=new AgentStore(AgentDirectory).Load();Check("routingSaveButtonPersistsCheckboxValues",saved.LocalFirst==local.IsChecked&&saved.OfferCloudHelp==offer.IsChecked);
        await Click(workbench,"Ask stronger model with my Agent draft");Check("strongerDraftButtonRequiresNonemptyRequest",UiDialogPathTests.Find<TextBlock>(workbench).Any(t=>t.Text.Contains("composer first")));
        await Click(workbench,"Agent conversation");Check("agentConversationButtonReturnsFromWorkbench",agentSurface.Content==agentView);
        UiPathTests.Click(returnToChat);Check("agentBackToChatButtonRestoresCodingChat",!AgentSurfaceActive&&chatView.Visibility==Visibility.Visible);
        var goalJob=new AgentTask{Title="UI goal fixture",Instructions="Synthetic acceptance only",ModelId=settings.Models[0].Id,ChatId=conversation.Id,ProjectId=project.Id,Status="Paused",Goal=new(){Objective="Synthetic accepted result",SuccessCriteria="User reviews synthetic output",CompletionMode="User acceptance",TurnBudget=2,State="Paused"}};
        Agent.Tasks.Add(goalJob);var pending=new AgentApproval{Kind="memory",Value="Synthetic goal review",TaskId=goalJob.Id};Agent.Approvals.Add(pending);SaveAgent();
        UiPathTests.Click(Find(this,"Pause current goal"));Check("goalBarPauseButtonRetainsResultsAndPausesGoal",goalJob.Goal.State=="Paused"&&goalJob.Status=="Paused");
        OpenAgent();view=agentView!;var automatic=UiDialogPathTests.Find<CheckBox>(view).Single(c=>Equals(c.Content,"Let the AI work automatically"));UiPathTests.Click(automatic);Check("trustedWorkCheckboxPersistsOnWithoutStartingTasks",new AgentStore(AgentDirectory).Load().TrustedWork&&!AgentIsWorking);UiPathTests.Click(automatic);Check("trustedWorkCheckboxPersistsOff",!new AgentStore(AgentDirectory).Load().TrustedWork);
        var more=UiDialogPathTests.Find<Expander>(view).Single(e=>Equals(e.Header,"More task actions"));var toggle=UiDialogPathTests.Find<System.Windows.Controls.Primitives.ToggleButton>(more).Single();UiPathTests.Click(toggle);Check("moreTaskActionsToggleShowsControls",more.IsExpanded);
        var taskList=UiDialogPathTests.Find<ListBox>(view).Single(l=>l.Items.OfType<AgentTask>().Any());taskList.SelectedItem=goalJob;
        await Click(view,"Resume selected task");Check("resumeTaskButtonCannotBypassReview",goalJob.Status=="Paused"&&!AgentIsWorking);
        await Click(view,"Stop selected task");Check("stopTaskButtonPersistsPausedState",goalJob.Status=="Paused"&&new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==goalJob.Id).Status=="Paused");
        await Click(view,"Pause work & watches");Check("pauseWorkAndWatchesButtonPersistsBothFlags",Agent.Paused&&Agent.WatchesPaused);
        await Click(view,"Resume watches");Check("resumeWatchesButtonChangesOnlyWatchFlag",!Agent.WatchesPaused&&Agent.Paused);await Click(view,"Pause work & watches");
        await Modal("taskDetailsButtonOpensReview",()=>UiPathTests.Click(Find(view,"Task details")),dialog=>{Check("taskDetailsShowsSelectedTaskInstructions",UiDialogPathTests.Find<TextBox>(dialog).Single().Text.Contains(goalJob.Instructions));UiPathTests.Click(Find(dialog,"Close"));});
        await Click(view,"Follow up selected task");Check("followUpTaskButtonSetsExactDestination",Agent.DraftTaskId==goalJob.Id);
        Field(view,"Message Infinity Agent").Text="Synthetic reviewed-task follow-up";await Click(view,"Send follow-up");Check("sendFollowUpButtonQueuesAtPendingReviewWithoutInference",goalJob.FollowUps.Last() is {Text:"Synthetic reviewed-task follow-up",Status:"Pending"}&&!AgentIsWorking);
        goalJob.FollowUps.Clear();SaveAgent();
        var attachment=Path.Combine(root,"AgentBrief.txt");File.WriteAllText(attachment,"SYNTHETIC_AGENT_ATTACHMENT");
        try
        {
            AttachmentPathsTest=()=>null;await Click(view,"Attach files and images");Check("agentAttachmentPickerCancelRetainsEmptyDraft",view.TestAttachmentCounts.Files==0);
            AttachmentPathsTest=()=>[attachment];await Click(view,"Attach files and images");Check("agentAttachButtonUsesChosenFile",view.TestAttachmentCounts.Files==1);
            AttachmentPathsTest=()=>[Path.Combine(root,"missing.txt")];await Click(view,"Attach files and images");Check("agentAttachmentErrorRetainsExistingDraft",view.TestAttachmentCounts.Files==1&&UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text.Contains("Could not attach files")));
        }
        finally{AttachmentPathsTest=null;}
        UiPathTests.Click(Find(view,"AgentBrief.txt · text"));Check("agentAttachmentPreviewButtonShowsActualText",documentText.Text.Contains("SYNTHETIC_AGENT_ATTACHMENT"));OpenAgent();view=agentView!;await Click(view,"Remove");Check("agentAttachmentRemoveButtonClearsDraft",view.TestAttachmentCounts.Files==0);
        var approvalList=UiDialogPathTests.Find<ListBox>(view).Single(l=>l.Items.OfType<AgentApproval>().Any());approvalList.SelectedItem=pending;
        await Modal("reviewApprovalButtonOpensSelectedReview",()=>UiPathTests.Click(Find(view,"Review approval")),dialog=>Check("reviewApprovalShowsExactMemoryProposal",dialog.Title=="Review Agent memory"&&UiDialogPathTests.Find<TextBox>(dialog).Single().Text==pending.Value));
        Check("closingApprovalReviewRecordsCancellationWithoutApplying",pending.Status=="Cancelled — nothing applied"&&Agent.Memory=="Synthetic persistent Agent memory");
        pending=new AgentApproval{Kind="memory",Value="Synthetic goal review",TaskId=goalJob.Id};Agent.Approvals.Add(pending);SaveAgent();approvalList.SelectedItem=pending;
        await Click(view,"Decline approval");Check("declineApprovalButtonRecordsNoApplication",pending.Status=="Declined"&&Agent.Memory=="Synthetic persistent Agent memory");
        pending=new AgentApproval{Kind="memory",Value="Synthetic goal review",TaskId=goalJob.Id};Agent.Approvals.Add(pending);SaveAgent();
        ShowChat();
        await Modal("savedGoalPauseButtonRoute",OpenGoalControls,dialog=>UiPathTests.Click(Find(dialog,"Pause")));Check("savedGoalPauseButtonPersistsPausedState",goalJob.Goal.State=="Paused"&&goalJob.Status=="Paused");
        await Modal("savedGoalResumeButtonRoute",OpenGoalControls,dialog=>UiPathTests.Click(Find(dialog,"Resume")));Check("savedGoalResumeCannotBypassPendingReview",goalJob.Status=="Paused"&&status.Text.Contains("pending",StringComparison.OrdinalIgnoreCase));
        await Modal("savedGoalAcceptCannotBypassPendingReview",OpenGoalControls,dialog=>UiPathTests.Click(Find(dialog,"Accept result")));Check("blockedGoalAcceptanceDoesNotClaimCompletion",goalJob.Goal.State=="Paused"&&goalJob.VerifiedAt==null);
        await Modal("savedGoalEditButtonRoute",OpenGoalControls,dialog=>
        {
            _=Dispatcher.BeginInvoke(()=>
            {
                var edit=Application.Current.Windows.OfType<GoalWindow>().Single(w=>w.IsVisible);Check("editGoalShowsPersistedObjective",Field(edit,"Goal").Text==goalJob.Goal.Objective);UiPathTests.Click(Find(edit,"Cancel"));
            });UiPathTests.Click(Find(dialog,"Edit goal"));
        });
        Check("cancelGoalEditPreservesExistingGoal",goalJob.Goal.Objective=="Synthetic accepted result");
        pending.Status="Declined";goalJob.Goal.State="Awaiting acceptance";goalJob.Status="Awaiting acceptance";goalJob.Detail="Actual synthetic output for UI review";SaveAgent();
        await Modal("savedGoalAcceptanceReviewRoute",OpenGoalControls,dialog=>
        {
            _=Dispatcher.BeginInvoke(()=>
            {
                var accept=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Accept goal result"&&w.IsVisible);Check("goalAcceptanceReviewIncludesActualResult",UiDialogPathTests.Find<TextBox>(accept).Single().Text.Contains(goalJob.Detail));UiPathTests.Click(Find(accept,"Approve this exact action"));
            });UiPathTests.Click(Find(dialog,"Accept result"));
        });
        Check("goalAcceptButtonPersistsUserAcceptanceSeparatelyFromVerification",goalJob.Goal.State=="Complete"&&goalJob.Status=="Accepted by user"&&goalJob.VerifiedAt==null);
    }
}
