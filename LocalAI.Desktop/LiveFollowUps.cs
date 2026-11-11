using System.IO;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private void QueueTaskFollowUp(AgentTask task,string text,string mode)
    {
        if(!Agent.Tasks.Contains(task))throw new IOException("Task no longer exists.");
        var item=new LiveFollowUp{Text=text,Mode=mode};item.Validate();if(task.FollowUps.Count>=100)throw new IOException("Follow-up history is full.");
        task.FollowUps.Add(item);var draft=Agent.Draft;var verified=task.VerifiedAt;task.VerifiedAt=null;Agent.Draft="";
        try{SaveAgent();}catch{task.FollowUps.Remove(item);task.VerifiedAt=verified;Agent.Draft=draft;throw;}
    }
    private void ApplyTaskFollowUps(AgentTask task,string? mode=null)
    {
        if(TaskNeedsReview(task))return;
        var pending=task.FollowUps.Where(f=>f.Status=="Pending"&&(mode==null||f.Mode==mode)).ToArray();if(pending.Length==0)return;
        var chat=conversations.SingleOrDefault(c=>c.Id==task.ChatId)??throw new IOException("Follow-up chat unavailable.");store.EnsureCurrent(chat);
        var messages=pending.Select(f=>new Message("user",f.Text,Images:f.Images,Files:f.Files)).ToArray();
        foreach(var message in messages){task.Messages.Add(message);chat.Messages.Add(message);}foreach(var item in pending)item.Status="Applied";
        task.VerifiedAt=null;task.VerificationSince=DateTimeOffset.UtcNow;
        try{store.Save(chat);SaveAgent();}
        catch{foreach(var message in messages){task.Messages.Remove(message);chat.Messages.Remove(message);}foreach(var item in pending)item.Status="Pending";store.EnsureCurrent(chat);store.Save(chat);throw;}
    }
    private readonly ComboBox chatFollowMode=new(){ItemsSource=new[]{"Queue","Steer"},SelectedIndex=0,Width=90,ToolTip="Queue after the reply, or steer the next safe step. A streamed chat reply stops with its partial text retained; an Agent applies steering before its next inference."};
    private bool chatSteering;
    private void RefreshFollowMode(){chatFollowMode.Visibility=busy||agentState?.Tasks.Any(t=>t.ChatId==conversation.Id&&t.Status is "Running" or "Queued")==true?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;if(busy)send.Content=chatFollowMode.SelectedItem?.ToString()??"Queue";}
    private void QueueChatFollowUp()
    {
        if(string.IsNullOrWhiteSpace(prompt.Text))return;
        try
        {
            var follow=new LiveFollowUp{Text=prompt.Text,Images=DraftImages.ToArray(),Files=DraftFiles.ToArray(),Mode=(string)chatFollowMode.SelectedItem};follow.Validate();if(conversation.FollowUps.Count>=100)throw new IOException("Follow-up history is full.");store.EnsureCurrent(conversation);
            conversation.FollowUps.Add(follow);try{store.Save(conversation);}catch{conversation.FollowUps.Remove(follow);throw;}
            prompt.Clear();DraftImages.Clear();DraftFiles.Clear();RefreshImageTray();status.Text=follow.Mode=="Steer"?"Steering saved. Taking effect at the next safe step; partial replies are retained.":"Follow-up saved for after this reply. Stop leaves it waiting for you.";if(follow.Mode=="Steer"&&chatRequestDepth>0&&active!=null){chatSteering=true;active.Cancel();}
        }
        catch(IOException e){status.Text=e.Message;}
    }
    private LiveFollowUp? dispatchFollowUp;
    private bool drainingChat;
    private async Task DrainChatFollowUps()
    {
        if(drainingChat||busy||AgentIsWorking)return;drainingChat=true;
        try
        {
            while(!busy&&conversation.FollowUps.Where(f=>f.Status=="Pending").OrderBy(f=>f.Mode=="Steer"?0:1).FirstOrDefault() is {} item)
            {
                if((prompt.Text.Length>0&&prompt.Text!=item.Text)||(DraftImages.Count>0&&!DraftImages.SequenceEqual(item.Images??[]))||(DraftFiles.Count>0&&!DraftFiles.SequenceEqual(item.Files??[]))){status.Text="A saved follow-up is waiting. Finish your draft, then choose Tools > Continue saved follow-ups.";break;}
                prompt.Text=item.Text;DraftImages.Clear();DraftImages.AddRange(item.Images??[]);DraftFiles.Clear();DraftFiles.AddRange(item.Files??[]);RefreshImageTray();
                var count=conversation.Messages.Count;dispatchFollowUp=item;try{await Send();}finally{dispatchFollowUp=null;}
                if(conversation.Messages.Skip(count).Any(m=>m.Role=="user"&&m.Content==item.Text)){item.Status="Applied";store.Save(conversation);}else break;
                if(conversation.ActivityState=="Stopped"||conversation.NeedsAttention)break;
            }
        }
        finally{drainingChat=false;}
    }
}
