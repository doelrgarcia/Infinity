using System.IO;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    internal bool AgentIsWorking=>agentRequest!=null||taskRequest!=null;
    internal bool AgentStopRequested=>agentRequest?.IsCancellationRequested==true||taskRequest?.IsCancellationRequested==true;
    private string? agentReviewWait;
    internal bool AgentAwaitingReview=>agentReviewWait!=null;
    internal string AgentWorkLabel=>agentReviewWait??(runningAgentTask is {} task?"Working on "+task.Title:agentRequest!=null?"Agent is working":"");
    internal void SaveAgentDestination(Guid? id,string draft)
    {
        if(id.HasValue&&!Agent.Tasks.Any(t=>t.Id==id))throw new IOException("This task is no longer available. Your prompt is retained.");
        var old=Agent.DraftTaskId;Agent.DraftTaskId=id;
        try{AgentSaveDraft(draft);}catch{Agent.DraftTaskId=old;throw;}
    }
    internal void AgentFollowUp(AgentTask task,string text,string mode="Queue",IReadOnlyList<ImageInput>? images=null,IReadOnlyList<FileAttachment>? files=null)
    {
        RequireTaskAvailable(task);
        if(!Agent.Tasks.Contains(task))throw new IOException("Task not found. Your prompt is retained.");
        if(string.IsNullOrWhiteSpace(text)||text.Length>16000)throw new IOException("Enter a follow-up of 1–16,000 characters.");
        FileAttachment.ValidateDraft(files??[]);ImageInput.RequireProvider(settings.Models.Single(p=>p.Id==task.ModelId),task.Messages.Concat([new("user",text,Images:images,Files:files)]));
        if((images?.Count>0||files?.Count>0)&&(task.Status is "Running" or "Queued"||runningAgentTask==task||agentRequest!=null||TaskNeedsReview(task)))throw new IOException("Wait or stop this task and resolve its reviews before attaching files. Your attachments are retained.");
        if(task.Status is "Running" or "Queued"||runningAgentTask==task||agentRequest!=null||TaskNeedsReview(task)){QueueTaskFollowUp(task,text,mode);return;}
        if(Agent.Approvals.Any(a=>a.TaskId==task.Id&&a.Status is "Pending review" or "Reviewing"))throw new IOException("Review this task's pending actions before sending its follow-up. Your prompt is retained.");
        var chat=conversations.SingleOrDefault(c=>c.Id==task.ChatId)??throw new IOException("The task's chat is unavailable. Your prompt is retained.");
        store.EnsureCurrent(chat);
        var message=new Message("user",text,Images:images,Files:files);var oldStatus=task.Status;var oldDetail=task.Detail;var oldVerified=task.VerifiedAt;var oldSince=task.VerificationSince;var oldDraft=Agent.Draft;var oldPaused=Agent.Paused;
        chat.Messages.Add(message);task.Messages.Add(message);task.VerifiedAt=null;task.VerificationSince=DateTimeOffset.UtcNow;task.Status="Queued";task.Detail="Follow-up received; waiting to continue this task.";Agent.Draft="";Agent.Paused=false;
        try{store.Save(chat);SaveAgent();}
        catch
        {
            task.Messages.RemoveAt(task.Messages.Count-1);chat.Messages.RemoveAt(chat.Messages.Count-1);task.Status=oldStatus;task.Detail=oldDetail;task.VerifiedAt=oldVerified;task.VerificationSince=oldSince;Agent.Draft=oldDraft;Agent.Paused=oldPaused;
            // Restore the visible chat as well; a stale store is never silently overwritten.
            store.EnsureCurrent(chat);store.Save(chat);throw;
        }
        history.Items.Refresh();if(conversation.Id==chat.Id)Render();_=PumpAgentTasks();
    }
}
