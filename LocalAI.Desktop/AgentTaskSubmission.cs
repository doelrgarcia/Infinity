using System.IO;
using System.Text.Json;
using System.Windows;
namespace LocalAI.Desktop;
using LocalAI.Core;
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid,string> taskReceipts=[];
    internal AgentTask SubmitAgentTask(AgentTaskRequest request)
    {
        request.Validate();EnsureAgentState();
        if(settings.Models.SingleOrDefault(m=>m.Id==request.ModelId) is not {IsCloud:false})throw new IOException("Task submission requires a configured local model. Cloud help remains a separate reviewed action.");
        if(Agent.Tasks.FirstOrDefault(t=>t.SubmissionId==request.RequestId) is { } existing)
        {if(existing.SubmissionDigest!=request.Digest)throw new IOException("This request ID already belongs to different instructions. Nothing restarted.");return existing;}
        if(Agent.Tasks.Count>=200)throw new IOException("Task history is full. No task started.");
        var project=new ProjectMemory{Name=request.Title,Roots=[request.ProjectRoot],Purpose=request.Instructions,UnfinishedWork="User-assigned task; build and device verification are not yet established."};SaveProject(project,false);
        var chat=new Conversation{Title=request.Title,ModelId=request.ModelId,ProjectId=project.Id,WorkspaceRoots=[request.ProjectRoot],Messages=[new("user",request.Instructions)]};store.Save(chat);conversations.Insert(0,chat);history.Items.Refresh();
        var task=new AgentTask{Title=request.Title,Instructions=request.Instructions,ModelId=request.ModelId,ProjectId=project.Id,ChatId=chat.Id,Status="Paused",Messages=[new("user",request.Instructions)],SubmissionId=request.RequestId,SubmissionDigest=request.Digest};Agent.Tasks.Add(task);Notice("Task submitted",task.Title+" — awaiting initial start.",task.Id);SaveAgent();return task;
    }
    internal async Task StartSubmittedTask(string path)
    {
        try
        {
            var request=AgentTaskRequest.Read(path);var receipt=Path.GetFullPath(path)+".status.json";var existed=agentState?.Tasks.Any(t=>t.SubmissionId==request.RequestId)==true;
            if(File.Exists(receipt)){using var old=JsonDocument.Parse(File.ReadAllText(receipt));if(old.RootElement.GetProperty("RequestId").GetGuid()!=request.RequestId)throw new IOException("Status file belongs to another request. Nothing started.");}
            EnsureAgentState();existed=Agent.Tasks.Any(t=>t.SubmissionId==request.RequestId);var task=SubmitAgentTask(request);taskReceipts[task.Id]=receipt;WriteTaskReceipts();await ApplyAppControl(new("switchChat",task.ChatId!.Value.ToString()));OpenAgent();
            if(!existed)AgentResume(task);else status.Text="Existing task located; it was not replayed. Use Resume selected task after reviewing its state.";
        }
        catch(Exception e){DarkTheme.Error("Task submission failed: "+e.Message);}
    }
    private void WriteTaskReceipts()
    {
        foreach(var entry in taskReceipts)
        {
            var task=Agent.Tasks.Single(t=>t.Id==entry.Key);var json=JsonSerializer.Serialize(new{RequestId=task.SubmissionId,TaskId=task.Id,task.ChatId,task.ProjectId,task.Title,task.Status,task.Detail,PendingApprovals=Agent.Approvals.Where(a=>a.TaskId==task.Id&&a.Status=="Pending review").Select(a=>new{a.Id,a.Kind}),Updated=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true});
            try{if(File.Exists(entry.Value)){using var current=JsonDocument.Parse(File.ReadAllText(entry.Value));if(current.RootElement.GetProperty("RequestId").GetGuid()!=task.SubmissionId)throw new IOException("Status file identity changed.");}var temp=entry.Value+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,json);File.Move(temp,entry.Value,true);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){status.Text="Task state saved; status sidecar could not update: "+e.Message;}
        }
    }
}
