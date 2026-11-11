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
    private async Task ExerciseGuidanceAndGoals(Dictionary<string,object> rows)
    {
        static bool Refused(Action action){try{action();return false;}catch(IOException){return true;}}
        chatProjectFilter=null;NewConversation();var root=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"guidance-goal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var nested=Path.Combine(root,"feature");Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root,"AGENTS.md"),"ROOT_GUIDANCE_MARKER: preserve all approvals.");File.WriteAllText(Path.Combine(nested,"AGENTS.md"),"NESTED_GUIDANCE_MARKER: verify the feature.");File.WriteAllText(Path.Combine(nested,"AGENTS.override.md"),"OVERRIDE_GUIDANCE_MARKER: use narrow changes.");
        var docs=ProjectGuidance.Load([root],[nested]);rows["guidanceLoadsRootsAndNearestOverrideInOrder"]=docs.Count==2&&docs[0].Content.Contains("ROOT_GUIDANCE_MARKER")&&docs[1].Content.Contains("OVERRIDE_GUIDANCE_MARKER")&&!docs.Any(d=>d.Content.Contains("NESTED_GUIDANCE_MARKER"));
        rows["guidanceNeverLoadsParentOrUnconnectedInstructions"]=ProjectGuidance.Load([nested]).Count==1&&ProjectGuidance.Load([]).Count==0;
        var large=Path.Combine(root,"large");Directory.CreateDirectory(large);File.WriteAllText(Path.Combine(large,"AGENTS.md"),new string('x',ProjectGuidance.MaximumBytes));rows["oversizedGuidanceFailsWithoutSilentTruncation"]=Refused(()=>ProjectGuidance.Load([large]));
        var sensitive=Path.Combine(root,"sensitive");Directory.CreateDirectory(sensitive);File.WriteAllText(Path.Combine(sensitive,"AGENTS.md"),"api_key=synthetic-private-value");rows["sensitiveGuidanceIsRefusedBeforeTransmission"]=Refused(()=>ProjectGuidance.Load([sensitive]));
        var project=new ProjectMemory{Name="Goal fixture",Roots=[root]};SaveProject(project);AttachProjectToCurrentChat(project.Id);files.Add(new(Path.Combine(nested,"Program.cs"),"SOURCE_MARKER"));
        rows["chatPacketsIncludeScopedInstructionsAndProvenance"]=BuildMessages("check").Any(m=>m.Role=="system"&&m.Content.Contains("ROOT_GUIDANCE_MARKER")&&m.Content.Contains("OVERRIDE_GUIDANCE_MARKER")&&m.Content.Contains("AGENTS.override.md"));files.Clear();
        var reduced=await AgentContext.Fit([new("system","controller"),new("user","scope"),new("system","REQUIRED_GUIDANCE"),new("assistant","old"),new("assistant","old2"),new("assistant","recent"),new("user","latest")],5,(packet,_)=>Task.FromResult(packet.Count(m=>!m.Content.Contains("older history messages omitted"))),CancellationToken.None);
        rows["contextFittingCannotDiscardInstructionMessages"]=reduced.Any(m=>m.Content=="REQUIRED_GUIDANCE");
        agentGuidanceDirectories.Clear();var count=Agent.Approvals.Count;var gated=await AgentTool(new("proposeFile",Target:Path.Combine(nested,"New.cs"),Content:"class New {}"),settings.Models[0].Id,null,CancellationToken.None);
        rows["newDirectoryGuidancePrecedesProposalReview"]=gated.Contains("No proposal was queued")&&Agent.Approvals.Count==count;
        rows["chatProposalLoadsNewDirectoryRulesBeforeAllowingReview"]=LoadChatProposalInstructions([nested])&&BuildMessages("revise").Any(m=>m.Content.Contains("OVERRIDE_GUIDANCE_MARKER"))&&!LoadChatProposalInstructions([nested]);
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var shutdown=new CancellationTokenSource();var scenario=0;var calls=0;var requestPackets=new List<string>();
        var fixtureFile=Path.Combine(root,"read.txt");File.WriteAllText(fixtureFile,"ACTUAL_READ_MARKER");
        var server=Task.Run(async()=>
        {
            while(!shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;try{context=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch(Exception e) when(e is OperationCanceledException or ObjectDisposedException or HttpListenerException){break;}
                try
                {
                    string data;
                    if(context.Request.Url!.AbsolutePath=="/health")data=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});
                    else
                    {
                        using var reader=new StreamReader(context.Request.InputStream);var packet=await reader.ReadToEndAsync();lock(requestPackets)requestPackets.Add(packet);var n=Interlocked.Increment(ref calls);
                        var answer=scenario switch{0=>n switch{1=>new AgentCall("readFile",fixtureFile),2=>new("reply","Read the actual file; continuing."),_=>new("requestGoalReview","Ready for the user to review the actual read result.")},1=>new("proposeFile",Target:Path.Combine(root,"reviewed.txt"),Content:"REVIEWED_GOAL_MARKER"),3=>n<12?new("readFile",fixtureFile):new("requestGoalReview","Twelve real steps completed."),4=>n==1?new("proposeFile",Target:Path.Combine(root,"accepted.txt"),Content:"EXACT_REVIEWED_GOAL_WRITE"):new("requestGoalReview","Applied file receipt inspected."),_=>new("reply","No tool work done.")};
                        data="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=JsonSerializer.Serialize(answer,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";
                    }
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(data));context.Response.Close();
                }
                catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){try{context.Response.Abort();}catch(ObjectDisposedException){}}
            }
        });
        var profile=new ModelProfile{Id="goal-loopback",Label="Goal loopback fixture",Engine="ryzen",Endpoint=$"http://127.0.0.1:{port}",ModelPath=root,ContextLimit=131072};settings.Models.Add(profile);model.Items.Refresh();model.SelectedItem=profile;
        async Task Idle(){for(var n=0;n<500&&pumping;n++)await Task.Delay(20);if(pumping)throw new IOException("Goal fixture did not reach a checkpoint.");}
        try
        {
            var goal=StartChatGoal(new(){Objective="Read fixture and prepare a factual result",SuccessCriteria="The actual file was read and the result is reviewed",CompletionMode="User acceptance"});await Idle();
            rows["goalContinuesAcrossRepliesIntoAnotherTurn"]=goal.Goal!.TurnsUsed==2&&calls==3&&goal.Messages.Any(m=>m.Content.Contains("ACTUAL_READ_MARKER"));
            rows["goalRequestsAcceptanceWithoutClaimingVerification"]=goal.Goal.State=="Awaiting acceptance"&&goal.Status=="Awaiting acceptance"&&goal.VerifiedAt==null&&goal.Detail.Contains("Ready for the user");
            rows["goalAndInstructionsReachRealInferencePacket"]=requestPackets.All(p=>p.Contains("ROOT_GUIDANCE_MARKER"))&&requestPackets.Any(p=>p.Contains("Persisted user goal"));
            rows["goalStateAndCriteriaPersistAcrossReload"]=new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==goal.Id).Goal is {State:"Awaiting acceptance",SuccessCriteria:"The actual file was read and the result is reviewed"};
            _=Dispatcher.BeginInvoke(()=>Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Accept goal result").Close());AcceptChatGoal();rows["cancelGoalAcceptanceDoesNotComplete"]=goal.Goal.State=="Awaiting acceptance";
            _=Dispatcher.BeginInvoke(()=>{var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Accept goal result");Descendants<Button>(dialog).Single(b=>Equals(b.Content,"Approve this exact action")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));});AcceptChatGoal();rows["explicitGoalAcceptanceIsDistinctFromVerifiedBuild"]=goal.Status=="Accepted by user"&&goal.Goal.State=="Complete"&&goal.VerifiedAt==null;
            scenario=1;calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;
            var reviewGoal=StartChatGoal(new(){Objective="Propose a fixture file",SuccessCriteria="User reviews exact write",CompletionMode="User acceptance"});await Idle();var approval=Agent.Approvals.Last(a=>a.TaskId==reviewGoal.Id);
            rows["goalYieldsImmediatelyForUnappliedReview"]=calls==1&&reviewGoal.Goal!.State=="Waiting for review"&&!File.Exists(Path.Combine(root,"reviewed.txt"));
            await AgentReview(approval,true);rows["decliningGoalReviewPausesWithoutRetry"]=reviewGoal.Goal!.State=="Paused"&&reviewGoal.Status=="Paused"&&calls==1;
            scenario=2;calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;
            var idleGoal=StartChatGoal(new(){Objective="Make actual progress",SuccessCriteria="A concrete result",CompletionMode="User acceptance"});await Idle();rows["goalStopsRepeatedNoProgressReplies"]=calls==2&&idleGoal.Goal!.State=="Needs attention"&&idleGoal.VerifiedAt==null;
            calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;var budget=StartChatGoal(new(){Objective="Bounded goal",SuccessCriteria="One turn then pause",CompletionMode="User acceptance",TurnBudget=1});await Idle();rows["goalTurnBudgetPausesWithoutFalseCompletion"]=calls==1&&budget.Goal!.State=="Paused"&&budget.Goal.TurnsUsed==1;
            var savedGoal=budget.Goal!.State;budget.Goal.State="Active";SaveAgent();rows["restartPausesGoalWithoutReplaying"]=new AgentStore(AgentDirectory).Load().Tasks.Single(t=>t.Id==budget.Id).Goal!.State=="Paused";budget.Goal.State=savedGoal;
            rows["checkedGoalRequiresConfiguredVerification"]=Refused(()=>StartChatGoal(new(){Objective="Build feature",SuccessCriteria="Tests pass"}));
            scenario=3;calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;var sliced=StartChatGoal(new(){Objective="Read through twelve steps",SuccessCriteria="Real tool receipts",CompletionMode="User acceptance"});await Idle();rows["goalContinuesPastTenToolStepsWithoutLosingHistory"]=calls==12&&sliced.Goal!.TurnsUsed==2&&sliced.Goal.State=="Awaiting acceptance"&&sliced.Messages.Count(m=>m.Content.StartsWith("Untrusted tool result for readFile"))==11;
            scenario=4;calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;var accepted=StartChatGoal(new(){Objective="Write only after exact approval",SuccessCriteria="Actual file receipt",CompletionMode="User acceptance"});await Idle();
            agentFileReviewTest=async review=>{await review.TestPreview();await review.TestApply();if(review.Receipt==null)review.Close();};try{await AgentReview(Agent.Approvals.Last(a=>a.TaskId==accepted.Id));await Idle();}finally{agentFileReviewTest=null;}
            rows["goalResumesAfterActualApprovedWriteAndInspectsReceipt"]=calls==2&&File.ReadAllText(Path.Combine(root,"accepted.txt"))=="EXACT_REVIEWED_GOAL_WRITE"&&accepted.Goal!.State=="Awaiting acceptance"&&accepted.Messages.Any(m=>m.Content.StartsWith("Reviewed action receipt (data):"));
            QueueTaskFollowUp(accepted,"REVIEW_RESULT_WITH_CORRECTION","Queue");AcceptChatGoal();rows["goalAcceptanceRefusesUnappliedFollowUpsWithoutOpeningConsent"]=accepted.Goal!.State=="Awaiting acceptance"&&accepted.Status!="Accepted by user";accepted.FollowUps.Clear();
            accepted.Goal!.State="Waiting for review";accepted.Goal.TurnBudget=accepted.Goal.TurnsUsed;GoalAfterReview(Agent.Approvals.Last(a=>a.TaskId==accepted.Id));rows["successfulReviewCannotOverrideExhaustedGoalBudget"]=accepted.Goal.State=="Paused"&&accepted.Status=="Paused"&&calls==2;
            scenario=2;calls=0;NewConversation();AttachProjectToCurrentChat(project.Id);model.SelectedItem=profile;await modelGate.WaitAsync();var pendingBudget=StartChatGoal(new(){Objective="Bounded goal with queued correction",SuccessCriteria="Keep the selected budget",CompletionMode="User acceptance",TurnBudget=1});AgentFollowUp(pendingBudget,"QUEUED_CANNOT_EXTEND_BUDGET");modelGate.Release();await Idle();rows["queuedFollowUpCannotOverrideGoalTurnBudget"]=pendingBudget.Goal!.State=="Paused"&&pendingBudget.Goal.TurnsUsed==1&&calls==1&&pendingBudget.FollowUps.Single().Status=="Pending";
            rows["pendingFollowUpsPreventVerifiedCompletion"]=AgentWorkspaceFeatures.Verification(pendingBudget,project,"irrelevant",false).Contains("saved follow-ups");
            var dialog=new GoalWindow(null,"PREPOPULATED_DRAFT"){Owner=this};var validated=false;dialog.Loaded+=(_,_)=>{Descendants<Button>(dialog).Single(b=>Equals(b.Content,"Start goal")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));validated=dialog.IsVisible&&Descendants<TextBlock>(dialog).Any(t=>t.Text.Contains("Invalid saved goal"));dialog.Close();};dialog.ShowDialog();rows["nativeGoalSetupRejectsMissingAcceptanceCriteria"]=validated;
            UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".goal.png"));
        }
        finally{AgentPause();shutdown.Cancel();listener.Stop();await server;}
    }
}
