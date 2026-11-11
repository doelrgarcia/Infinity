using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly SemaphoreSlim modelGate=new(1,1);
    private AgentStore? agentStore;
    private AgentState? agentState;
    private AgentView? agentView;
    private BrowserWindow? agentBrowser;
    private CancellationTokenSource? agentRequest,taskRequest;
    private AgentTask? runningAgentTask;
    private bool pumping,agentClosing;
    private int agentReviewDepth;
    private Action<FileActionReviewWindow>? agentFileReviewTest;
    private Action<FolderBatchReviewWindow>? agentFolderReviewTest;
    private Action<CommandWindow>? agentCommandReviewTest;
    private DispatcherTimer? agentWatchTimer;
    private string AgentDirectory=>Path.Combine(smokeOutput==null?AppPaths.AgentRoot:Path.Combine(Path.GetDirectoryName(smokeOutput)!,"agent"));
    private AgentState Agent=>agentState??throw new IOException("Open Agent first.");
    private void SaveAgent(){Agent.Activity=AgentAwaitingReview?AgentWorkLabel:AgentIsWorking?AgentStopRequested?"Stopping":AgentWorkLabel:Agent.Paused?"Paused · saved work retained":"Ready · no active request";agentStore!.Save(Agent);WriteTaskReceipts();if(agentEntry!=null)agentEntry.Content="Tasks"+(Agent.Inbox.Count(n=>!n.Read)>0?$" • {Agent.Inbox.Count(n=>!n.Read)} updates":"");RefreshGoalBar();RefreshFollowMode();agentView?.Refresh();agentWorkbench?.Refresh();}
    internal void OpenAgent()
    {
        try
        {
            EnsureAgentState();if(agentWatchTimer==null){SaveAgent();agentWatchTimer=new(){Interval=TimeSpan.FromSeconds(2)};agentWatchTimer.Tick+=(_,_)=>{try{PollAgentWatches();}catch(Exception e){agentWatchTimer.Stop();status.Text="Agent monitoring paused: "+e.Message;}};agentWatchTimer.Start();}
            agentView??=new(this,Agent,settings.Models,(model.SelectedItem as ModelProfile)?.Id);ShowAgentSurface(agentView);agentView.Refresh();agentView.FocusComposer();
        }
        catch(Exception e){DarkTheme.Error("Agent could not open: "+e.Message);}
    }
    internal void AgentSaveDraft(string draft){Agent.Draft=draft;SaveAgent();}
    internal void SetAgentTrustedWork(bool enabled)
    {
        EnsureAgentState();
        if(Agent.TrustedWork==enabled)return;
        Agent.TrustedWork=enabled;if(enabled)Agent.TrustedDeletes=0;
        Notice("Trusted work",enabled?"Trusted work enabled. File edits and non-destructive commands run automatically in connected folders; folder/mass deletes, destructive commands and browser form actions still require review.":"Trusted work disabled. File edits, commands and deletes require review again.",null);
        status.Text=Agent.TrustedWork?"Trusted work on: the Agent can edit files and run non-destructive commands in connected folders. Mass/folder deletes and destructive commands still wait for your review.":"Trusted work off: the Agent's file changes and commands wait for review.";
        SaveAgent();
    }
    internal async Task AgentSend(string text,string profileId,bool forceCloud=false,IReadOnlyList<ImageInput>? images=null,IReadOnlyList<FileAttachment>? files=null)
    {
        if(agentRequest!=null)throw new IOException("Wait for this Agent reply or press Stop reply.");
        if(string.IsNullOrWhiteSpace(text))return;
        agentRequest=new();
        try{FileAttachment.ValidateDraft(files??[]);ImageInput.RequireProvider(settings.Models.Single(p=>p.Id==profileId),Agent.Messages.Concat([new("user",text,Images:images,Files:files)]));Agent.Messages.Add(new("user",text,Images:images,Files:files));Agent.Draft="";SaveAgent();await RunAgent(Agent.Messages,profileId,null,agentRequest.Token,forceCloud);}
        catch(Exception e){Agent.Messages.Add(new("assistant",JsonSerializer.Serialize(new{action="reply",value=e is OperationCanceledException?"Agent reply stopped. Recorded tool outcomes and pending reviews are retained.":"Agent needs attention: "+e.Message})));SaveAgent();}
        finally{agentRequest.Dispose();agentRequest=null;SaveAgent();}
    }
    private async Task RunAgent(List<Message> messages,string profileId,AgentTask? task,CancellationToken token,bool forceCloud=false)
    {
        if(await AnswerAgentActionQuestion(messages,profileId,task,token,forceCloud))return;
        agentGuidanceDirectories.Clear();
        var scope=JsonSerializer.Serialize(new{memory=Agent.Memory,project=AgentProject(task),connectedFolders=AgentRoots(task),taskId=task?.Id,targetChat=task?.ChatId,canDelegate=task==null});
        string reply;try{reply=await AgentProtocol.Run(messages,scope,(packet,ct)=>{var enriched=packet.ToList();if(task?.Goal!=null)enriched.Add(new("system",GoalSystemText(task)));if(ProjectGuidance.Message(AgentRoots(task),agentGuidanceDirectories) is { } guidance)enriched.Insert(2,guidance);if(SkillLibrary.ForRequest(AgentRoots(task),task?.Goal!=null?task.Goal.Objective+"\n"+string.Join("\n",messages.Where(AgentContext.IsRequest).Select(m=>m.Content)):messages.LastOrDefault(AgentContext.IsRequest)?.Content??"") is {} skillContext)enriched.Insert(2,skillContext);var projected=AgentContext.ForModel(enriched,Agent.Approvals.Where(a=>a.ProjectId==AgentProject(task)?.Id));return forceCloud?AgentGenerate(profileId,projected,ct):AgentGenerateWithRouting(profileId,projected,ct);},(call,ct)=>AgentTool(call,profileId,task,ct),SaveAgent,token,task?.Goal==null?null:()=>TaskNeedsReview(task)||task.Goal.State!="Active",()=>{if(task!=null)ApplyTaskFollowUps(task,"Steer");if(ActionQuestions.IsActionQuestion(messages.LastOrDefault(AgentContext.IsRequest)?.Content??""))throw new ActionQuestionCheckpoint();},Agent.TrustedWork?AgentProtocol.TrustedInstructions:null);}
        catch(ActionQuestionCheckpoint){await AnswerAgentActionQuestion(messages,profileId,task,token,forceCloud);return;}
        if(task!=null)
        {
            task.Status=task.VerifiedAt.HasValue?"Verified complete":Agent.Approvals.Any(a=>a.TaskId==task.Id&&a.Status=="Pending review")?"Waiting for review":"Response ready";
            task.Detail=reply;var target=conversations.Single(c=>c.Id==task.ChatId);store.EnsureCurrent(target);target.Messages.Add(new("assistant",reply,profileId));ChatMonitor.Record(target,task.Status=="Waiting for review"?"Needs attention":"Response ready","Agent task returned a response; project completion is not independently verified.",task.Status=="Waiting for review");store.Save(target);if(conversation.Id==target.Id)Render();history.Items.Refresh();
        }
        SaveAgent();
    }
    private async Task<string> AgentGenerate(string id,IReadOnlyList<Message> packet,CancellationToken token)
    {
        var profile=settings.Models.Single(p=>p.Id==id);ImageInput.RequireProvider(profile,packet);await modelGate.WaitAsync(token);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(settings.GenerationTimeoutSeconds));
            if(profile.IsCloud)
            {
                if(!File.Exists(profile.Executable))throw new IOException("Provider CLI is missing. Install it in AI apps & CLI.");
                if(CliAssistant.FormatPrompt(packet).Length>CliAssistant.MaximumPromptCharacters)throw new IOException("Agent cloud context is full. Shorten the task or use a larger local context.");
                agentReviewWait="Review needed · approve the exact cloud request or cancel to keep it local";SaveAgent();
                try{if(new ProviderConsent(profile,packet){Owner=this}.ShowDialog()!=true)throw new OperationCanceledException("Cloud request declined.",token);}
                finally{agentReviewWait=null;SaveAgent();}
            }
            else
            {
                using var load=CancellationTokenSource.CreateLinkedTokenSource(token);load.CancelAfter(TimeSpan.FromSeconds(settings.LoadTimeoutSeconds));await runtime.Ensure(profile,client,load.Token);
                packet=await AgentContext.Fit(packet,profile.ContextLimit-1536,(p,ct)=>client.CountTokens(profile,p,ct),timeout.Token);
            }
            var result=profile.IsCloud?await cli.Generate(profile,packet,_=>{},timeout.Token):await client.Generate(profile,packet,1536,0.1,_=>{},timeout.Token,true);
            if(result.FinishReason=="length")throw new IOException("Agent response reached its output limit; no partial action was executed.");return result.Text;
        }
        finally{modelGate.Release();}
    }
    private Conversation AgentChat(string value)=>Guid.TryParse(value,out var id)?VisibleChats.SingleOrDefault(c=>c.Id==id)??throw new IOException("Chat not found or deleted."):throw new IOException("Use an exact chat ID from listChats.");
    private async Task<string> AgentTool(AgentCall call,string modelId,AgentTask? task,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var roots=AgentRoots(task);
        if(ObserveInstructionScope(call,roots) is { } instructionNotice)return instructionNotice;
        string Receipt(object value)=>JsonSerializer.Serialize(value);
        switch(call.Action)
        {
            case "requestGoalReview":
                if(task?.Goal is not {State:"Active",CompletionMode:"User acceptance"} goal)throw new IOException("Only an active user-acceptance goal can request this review.");
                goal.State="Awaiting acceptance";task.Detail=call.Value;SaveAgent();return "Waiting for explicit user acceptance; not verified complete.";
            case "listTasks":return Receipt(Agent.Tasks.Select(t=>new{t.Id,t.Title,t.Status,t.Detail,t.ChatId}));
            case "pauseTask":case "resumeTask":
                if(task!=null)throw new IOException("Background tasks cannot control the task queue.");
                var targetTask=Guid.TryParse(call.Target,out var taskId)?Agent.Tasks.SingleOrDefault(t=>t.Id==taskId):null;
                if(targetTask==null)throw new IOException("Use an exact task ID from listTasks.");
                if(call.Action=="pauseTask")AgentCancelTask(targetTask);else AgentResume(targetTask);return Receipt(new{targetTask.Id,targetTask.Status,note="Cancellation is requested for a running task; inspect its next status update."});
            case "listChats":return Receipt(ChatSnapshot());
            case "readChat":{var chat=AgentChat(call.Target);return Receipt(new{chat.Id,chat.Title,chat.TaskBrief,messages=chat.Messages.TakeLast(12)});}
            case "newChat":
            case "delegate":
            {
                if(task!=null)throw new IOException("Delegated tasks cannot create or delegate more chats.");
                if(call.Action=="delegate"&&(Agent.Tasks.Count>=200||call.Content.Length is <1 or >16000))throw new IOException("Task limit reached or invalid task instructions.");
                if(call.Value.Length is <1 or >120)throw new IOException("Use a task/chat title between 1 and 120 characters.");
                var chat=string.IsNullOrEmpty(call.Target)?new Conversation{Title=call.Value,ModelId=modelId,WorkspaceRoots=[..roots],ProjectId=AgentProject(task)?.Id}:AgentChat(call.Target);
                if(!conversations.Contains(chat)){store.Save(chat);conversations.Insert(0,chat);history.Items.Refresh();}
                if(call.Action=="newChat")return Receipt(new{chat.Id,chat.Title,status="Created"});
                if(chat.Id==conversation.Id||Agent.Tasks.Any(t=>t.ChatId==chat.Id&&t.Status is "Running" or "Queued"))throw new IOException("Choose a separate chat that has no queued or running task.");
                store.EnsureCurrent(chat);chat.Messages.Add(new("user",call.Content));store.Save(chat);
                var job=new AgentTask{Title=call.Value,Instructions=call.Content,ModelId=modelId,ChatId=chat.Id,ProjectId=AgentProject(task)?.Id,Messages=[new("user",call.Content)]};Agent.Tasks.Add(job);Agent.Paused=false;SaveAgent();_=PumpAgentTasks();return Receipt(new{job.Id,chatId=chat.Id,status=job.Status});
            }
            case "openChat":case "renameChat":case "stopChat":
                if(task!=null)throw new IOException("A background task cannot switch, rename or stop your foreground chat.");
                await ChatAssistantAction(call.Action=="openChat"?"open":call.Action=="renameChat"?"rename":"stop",AgentChat(call.Target).Id,call.Value);return "Chat action applied.";
            case "watchChat":{var chat=AgentChat(call.Target);Agent.Watches[chat.Id]=WatchFingerprint(chat);Agent.WatchesPaused=false;SaveAgent();return "Watching real recorded activity changes while Infinity is running.";}
            case "unwatchChat":Agent.Watches.Remove(AgentChat(call.Target).Id);SaveAgent();return "Watch removed.";
            case "listFiles":return Receipt(await Task.Run(()=>new FileWorkspace(roots).List(call.Value,token),token));
            case "readFile":return Receipt(await Task.Run(()=>new FileWorkspace(roots).Read(call.Value,token),token));
            case "browserOpen":
                var url=BrowserAddress.Normalize(call.Value);if(agentBrowser==null){agentBrowser=new BrowserWindow(null,url,testProfile:smokeOutput==null?null:Path.Combine(AgentDirectory,"browser-fixture-"+Guid.NewGuid().ToString("N")));agentBrowser.Closed+=(_,_)=>agentBrowser=null;ShowRightPane("Browser",agentBrowser.DockContent(this));}else{ShowRightPane("Browser",agentBrowser.DockContent(this));agentBrowser.Navigate(url);}return "Navigation requested: "+url+". Read the page to verify its actual URL and content.";
            case "browserRead":return await (agentBrowser??throw new IOException("Open an Agent browser page first.")).AgentRead(token);
            case "browserLinks":return await (agentBrowser??throw new IOException("Open a page first.")).Control("links");
            case "browserFollow":return await (agentBrowser??throw new IOException("Open a page first.")).FollowLink(call.Value);
            case "proposeFiles":
                var actions=FileActionProtocol.Parse(call.Value);foreach(var action in actions)new FileWorkspace(roots).Resolve(action.Path);
                return Agent.TrustedWork?await TrustedFiles(actions,task,roots,token):QueueApproval("files",call.Value,task);
            case "proposeFolder":
                new FileWorkspace(roots).Resolve(call.Value);
                var folderAction=new FileAction("mkdir",call.Value);
                return Agent.TrustedWork?await TrustedFiles([folderAction],task,roots,token):QueueApproval("files",Receipt(new[]{new{operation="mkdir",path=call.Value}}),task);
            case "proposeFile":
                new FileWorkspace(roots).Resolve(call.Target);if(!ContextFiles.Allowed(call.Target))throw new IOException("Only screened source/text destinations are allowed.");
                var writeAction=new FileAction("write",call.Target,Content:call.Content);
                return Agent.TrustedWork?await TrustedFiles([writeAction],task,roots,token):QueueApproval("files",Receipt(new[]{new{operation="write",path=call.Target,content=call.Content}}),task);
            case "proposeCommand":
                new CommandRunner(roots).Preview(new(call.Value,call.Target));
                if(Agent.TrustedWork&&!TrustedWorkPolicy.IsDestructiveCommand(call.Value))return await TrustedCommand(call,task,roots,token);
                var commandApproval=QueueApproval("command",Receipt(new CommandRequest(call.Value,call.Target)),task);Agent.Approvals.Last().Purpose=call.Content is "build" or "test"?call.Content:"";SaveAgent();return commandApproval;
            case "remember":if(call.Value.Length>16000)throw new IOException("Memory limit is 16,000 characters.");return QueueApproval("memory",call.Value,task);
            default:return await AgentFeatureTool(call,modelId,task,token);
        }
    }
    private async Task<string> TrustedFiles(IReadOnlyList<FileAction> actions,AgentTask? task,IReadOnlyList<string> roots,CancellationToken token)
    {
        var applied=new List<FileActionReceipt>();var pending=new List<FileAction>();var deleteCount=actions.Count(a=>a.Operation=="delete");
        foreach(var action in actions)
        {
            token.ThrowIfCancellationRequested();
            if(TrustedWorkPolicy.RequiresDeleteReview(action.Operation,deleteCount,Agent.TrustedDeletes)){pending.Add(action);continue;}
            var approval=new AgentApproval{Kind="files",Value=JsonSerializer.Serialize(new[]{action}),Status="Auto-applied (trusted)",TaskId=task?.Id,ProjectId=AgentProject(task)?.Id,Roots=[..roots],Purpose="trusted work"};
            Agent.Approvals.Add(approval);
            var review=new FileActionReviewWindow(new FileWorkspace(roots),action){Headless=true};TrackedChange? tracked=null;
            review.BeforeApply=plan=>tracked=BeginTrackedChange(approval,0,plan);
            await review.TestPreview();await review.TestApply();
            if(review.Receipt is not { } receipt||tracked==null)
            {
                approval.Status="Needs attention — inspect receipts";Notice("Trusted work failed",action.Operation+" "+action.Path+" did not return an applied receipt.",task?.Id,approval.Id);SaveAgent();
                throw new IOException("Trusted file action did not complete; inspect the recorded change before retrying.");
            }
            CompleteTrackedChange(tracked,approval,receipt);if(action.Operation=="delete")Agent.TrustedDeletes++;applied.Add(receipt);
            Notice("Trusted work",action.Operation+" applied: "+action.Path,task?.Id,approval.Id);
        }
        string? pendingReview=null;
        if(pending.Count>0)pendingReview=QueueApproval("files",JsonSerializer.Serialize(pending),task,AgentProject(task)?.Id,roots);
        SaveAgent();
        return JsonSerializer.Serialize(new{status=pending.Count==0?"Auto-applied (trusted)":"Applied non-delete actions; delete(s) need review",applied,pendingReview,instruction="Only the applied receipts are actual outcomes. Pending delete actions were not applied and need user approval."});
    }
    private async Task<string> TrustedCommand(AgentCall call,AgentTask? task,IReadOnlyList<string> roots,CancellationToken token)
    {
        var runner=new CommandRunner(roots,smokeOutput==null?null:Path.Combine(AgentDirectory,"command-logs"));var plan=runner.Preview(new(call.Value,call.Target));
        var approval=new AgentApproval{Kind="command",Value=JsonSerializer.Serialize(plan.Request),Status="Auto-run (trusted)",TaskId=task?.Id,ProjectId=AgentProject(task)?.Id,Purpose=call.Content is "build" or "test"?call.Content:"",Roots=[..roots]};Agent.Approvals.Add(approval);
        var project=Agent.Projects.SingleOrDefault(p=>p.Id==approval.ProjectId);var before=approval.Purpose is "build" or "test"&&project!=null?await Task.Run(()=>AgentWorkspaceFeatures.Snapshot(project)):"";
        try
        {
            var run=await runner.Run(plan,true,null,token);await RecordCommandEvidence(approval,run,before);approval.Status=run.ExitCode==0?"Auto-run (trusted)":"Auto-run (trusted) — exit "+run.ExitCode;
            Notice("Trusted work","Command "+run.Status+" (exit "+(run.ExitCode?.ToString()??"unavailable")+")",task?.Id,approval.Id);SaveAgent();
            return JsonSerializer.Serialize(new{approval.Id,status=approval.Status,commandStatus=run.Status,run.ExitCode,output=run.Output.Length>16000?run.Output[^16000..]:run.Output,log=run.LogPath,instruction="Actual command receipt; inspect exit code and output. This is the only execution proof."});
        }
        catch(Exception e){approval.Status="Needs attention — inspect receipts";Notice("Trusted work failed","Command did not complete: "+e.Message,task?.Id,approval.Id);SaveAgent();throw;}
    }
    private string QueueApproval(string kind,string value,AgentTask? task,Guid? projectId=null,IEnumerable<string>? roots=null)
    {
        if(Agent.Approvals.Count(a=>a.Status=="Pending review")>=50)throw new IOException("Review pending approvals before adding more.");
        var approval=new AgentApproval{Kind=kind,Value=value,TaskId=task?.Id,ProjectId=projectId??AgentProject(task)?.Id,Roots=(roots??AgentRoots(task)).ToList()};if(task!=null)task.VerifiedAt=null;Agent.Approvals.Add(approval);Notice("Approval needed",AgentPresentation.ApprovalLabel(approval).Replace('\n',' '),task?.Id,approval.Id);SaveAgent();return JsonSerializer.Serialize(new{approval.Id,status=approval.Status,instruction="User must open Approvals. Nothing applied or executed."});
    }
    private async Task PumpAgentTasks()
    {
        if(pumping||Agent.Paused||agentClosing)return;pumping=true;
        try
        {
            while(!Agent.Paused&&!agentClosing&&Agent.Tasks.FirstOrDefault(t=>t.Status=="Queued") is { } job)
            {
                runningAgentTask=job;taskRequest=new();job.Status="Running";SaveAgent();
                try{RequireTaskAvailable(job);ApplyTaskFollowUps(job);if(job.Goal is {State:"Active"} goal){goal.TurnsUsed++;SaveAgent();}await RunAgent(job.Messages,job.ModelId,job,taskRequest.Token,job.UseSelectedProvider);if(job.Goal!=null)await FinishGoalTurn(job);if(job.FollowUps.Any(f=>f.Status=="Pending")&&!TaskNeedsReview(job)&&!Agent.Paused&&!taskRequest.IsCancellationRequested&&(job.Goal==null||job.Goal.State is "Active" or "Awaiting acceptance"&&job.Goal.TurnsUsed<job.Goal.TurnBudget)){job.Status="Queued";if(job.Goal is {} continued)continued.State="Active";}}
                catch(AgentStepLimitException) when(job.Goal is {State:"Active"}){job.Detail="Goal checkpoint: tool step slice ended; actual receipts retained.";await FinishGoalTurn(job);}
                catch(Exception e){if(job.Goal is {} goal){goal.State=e is OperationCanceledException?"Paused":"Needs attention";goal.Updated=DateTimeOffset.UtcNow;}job.Status=e is OperationCanceledException?"Paused":"Needs attention";job.Detail=e is OperationCanceledException?"Stopped; completed tool outcomes retained. Resume explicitly to continue.":e.Message;}
                finally{Notice(job.Status,job.Title+" — "+job.Detail,job.Id);taskRequest.Dispose();taskRequest=null;runningAgentTask=null;Agent.Messages.Add(new("assistant",JsonSerializer.Serialize(new{action="reply",value=$"Task ‘{job.Title}’: {job.Status}. {job.Detail}"})));SaveAgent();}
            }
        }
        catch(Exception e){Agent.Paused=true;status.Text="Agent task queue paused: "+e.Message;}
        finally{pumping=false;agentView?.Refresh();}
    }
    internal void AgentPause(){foreach(var task in Agent.Tasks.Where(t=>t.Goal is {State:"Active" or "Waiting for review"})){task.Goal!.State="Paused";if(task.Status=="Queued")task.Status="Paused";}Agent.Paused=true;Agent.WatchesPaused=true;taskRequest?.Cancel();agentRequest?.Cancel();SaveAgent();}
    internal void AgentResumeWatches(){Agent.WatchesPaused=false;SaveAgent();}
    internal void AgentStopReply()=>agentRequest?.Cancel();
    internal void AgentResume(AgentTask? task)
    {
        if(task==null)throw new IOException("Select a task to resume.");RequireTaskAvailable(task);
        if(task.Status=="Running")return;if(Agent.Approvals.Any(a=>a.TaskId==task.Id&&a.Status is "Pending review" or "Reviewing"))throw new IOException("Review this task's pending approvals first.");
        if(task.Goal is {} goal){if(TaskNeedsReview(task))throw new IOException("Resolve pending or uncertain reviews before resuming the goal.");goal.State="Active";goal.TurnsUsed=0;goal.IdleTurns=0;goal.ProgressHash=AgentGoal.Fingerprint(task);}task.VerifiedAt=null;task.Status="Queued";Agent.Paused=false;SaveAgent();_=PumpAgentTasks();
    }
    internal void AgentCancelTask(AgentTask? task)
    {if(task==null)return;if(task.Goal is {} goal)goal.State="Paused";if(task==runningAgentTask){taskRequest?.Cancel();SaveAgent();return;}task.Status="Paused";task.Detail="Stopped by user.";SaveAgent();}
    internal async Task AgentReview(AgentApproval approval,bool decline=false)
    {
        if(agentReviewDepth>0)throw new IOException("Finish the current action review first.");
        if(approval.Status!="Pending review")throw new IOException("This approval has already been handled. Inspect its recorded outcome before requesting a new action.");
        if(decline){approval.Status="Declined";ApprovalResult(approval,"User declined; nothing applied.");GoalAfterReview(approval);return;}
        approval.Status="Reviewing";SaveAgent();var receipts=new List<string>();int? fileCount=null;string? reviewFailure=null;agentReviewDepth++;
        try
        {
            var owner=this;var roots=approval.Roots.Count>0?approval.Roots:AgentRoots();
            if(approval.Kind=="memory")
            {
                if(AgentView.ReviewText(owner,"Review Agent memory",approval.Value,true) is { } memory){Agent.Memory=memory;receipts.Add("Approved memory saved.");}
            }
            else if(approval.Kind=="files")
            {
                var actions=FileActionProtocol.Parse(approval.Value);fileCount=actions.Count;if(actions.Any(a=>a.Operation is "delete" or "rmdir"))Agent.TrustedDeletes=0;var individually=true;
                if(actions.Count>1&&actions.All(a=>a.Operation=="mkdir"))
                {
                    var group=new FolderBatchReviewWindow(new FileWorkspace(roots),actions){Owner=owner};var tracked=new Dictionary<int,TrackedChange>();
                    group.BeforeApply=(index,plan)=>tracked[index]=BeginTrackedChange(approval,index,plan);group.AfterApply=(index,receipt)=>CompleteTrackedChange(tracked[index],approval,receipt);
                    if(agentFolderReviewTest!=null)group.Loaded+=(_,_)=>agentFolderReviewTest(group);group.ShowDialog();receipts.AddRange(group.Receipts.Select(r=>JsonSerializer.Serialize(r)));reviewFailure=group.Failure;individually=group.ReviewIndividually&&receipts.Count==0;
                }
                if(individually)foreach(var pair in actions.Select((action,index)=>(action,index)))
                {var review=new FileActionReviewWindow(new FileWorkspace(roots),pair.action){Owner=owner};AttachChangeTracking(review,approval,pair.index);if(agentFileReviewTest!=null)review.Loaded+=(_,_)=>agentFileReviewTest(review);if(review.ShowDialog()!=true||review.Receipt==null)break;receipts.Add(JsonSerializer.Serialize(review.Receipt));}
            }
            else if(approval.Kind=="command")
            {var request=JsonSerializer.Deserialize<CommandRequest>(approval.Value)??throw new IOException("Invalid command proposal.");var project=Agent.Projects.SingleOrDefault(p=>p.Id==approval.ProjectId);var before=approval.Purpose is "build" or "test"&&project!=null?await Task.Run(()=>AgentWorkspaceFeatures.Snapshot(project)):"";var review=new CommandWindow(roots,request,logRoot:smokeOutput==null?null:Path.Combine(AgentDirectory,"command-logs")){Owner=owner};if(agentCommandReviewTest!=null)review.Loaded+=(_,_)=>agentCommandReviewTest(review);review.ShowDialog();if(review.Result!=null){receipts.Add(JsonSerializer.Serialize(review.Result));await RecordCommandEvidence(approval,review.Result,before);}}
            else if(approval.Kind=="project")
            {var project=Agent.Projects.Single(p=>p.Id==approval.ProjectId);var edited=AgentView.ReviewText(owner,"Review project memory proposal",approval.Value,true);if(edited!=null){var updated=AgentWorkspaceFeatures.ProposedNotes(project,edited);SaveProject(updated,false);receipts.Add("Reviewed project notes saved.");}}
            else if(approval.Kind=="browser")
            {var plan=JsonSerializer.Deserialize<BrowserActionPlan>(approval.Value)??throw new IOException("Invalid browser proposal.");if(AgentView.Confirm(owner,"Review browser interaction",plan.Review))receipts.Add(await (agentBrowser??throw new IOException("Browser session ended. List elements and request a new review.")).ApplyAgentAction(plan,true));}
            approval.Status=reviewFailure!=null?"Needs attention — inspect receipts":receipts.Count==0?"Cancelled — nothing applied":fileCount.HasValue&&receipts.Count<fileCount?"Partially applied — inspect receipts":"Review finished — inspect receipts";ApprovalResult(approval,string.Join("\n",receipts.DefaultIfEmpty("Review cancelled. Nothing applied."))+(reviewFailure==null?"":"\nReview stopped: "+reviewFailure));
        }
        catch(Exception e){approval.Status="Needs attention — inspect receipts";ApprovalResult(approval,string.Join("\n",receipts)+"\nReview failed: "+e.Message);}
        finally{agentReviewDepth--;}
        GoalAfterReview(approval);await Task.CompletedTask;
    }
    private void ApprovalResult(AgentApproval approval,string result)
    {
        var evidence="Approval "+approval.Id+" ("+approval.Kind+"): "+approval.Status+"\n"+result;
        Agent.Messages.Add(new("user","Reviewed action receipt (data): "+evidence));
        if(Agent.Tasks.SingleOrDefault(t=>t.Id==approval.TaskId) is { } task){task.Messages.Add(new("user","Reviewed action receipt (data): "+evidence));task.Detail=evidence;}
        SaveAgent();
    }
    private static string WatchFingerprint(Conversation chat)=>JsonSerializer.Serialize(new{chat.ActivityState,chat.ActivityDetail,chat.LastActivityUtc,chat.NeedsAttention,chat.Messages.Count});
    private void PollAgentWatches()
    {
        if(Agent.WatchesPaused)return;var changed=false;
        foreach(var id in Agent.Watches.Keys.ToArray())
        {var chat=conversations.SingleOrDefault(c=>c.Id==id);if(chat==null||IsChatDeleted(chat))continue;var fingerprint=WatchFingerprint(chat);if(fingerprint==Agent.Watches[id])continue;Agent.Watches[id]=fingerprint;Agent.Messages.Add(new("assistant",JsonSerializer.Serialize(new{action="reply",value=$"Activity update: {chat.Title} — {chat.ActivityState}. {chat.ActivityDetail}"})));changed=true;}
        if(changed){Notice("Chat activity","A watched chat changed. Read its activity update in the Agent conversation.");SaveAgent();}
    }
    private void CloseAgent(){agentClosing=true;agentWatchTimer?.Stop();agentRequest?.Cancel();taskRequest?.Cancel();agentView?.StopVoice();agentWorkbench?.CancelSearch();agentBrowser?.Close();agentSurface.Content=null;agentView=null;agentWorkbench=null;}
}
