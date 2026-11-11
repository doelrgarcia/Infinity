using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class ActionQuestionCheckpoint:IOException;
public sealed partial class MainWindow
{
    private string AgentOfferScope(AgentTask? task)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{task=task?.Id,project=AgentProject(task)?.Id,roots=AgentRoots(task)}))));
    private async Task<bool> AnswerAgentActionQuestion(List<Message> messages,string profileId,AgentTask? task,CancellationToken token,bool forceCloud)
    {
        var question=messages.LastOrDefault(AgentContext.IsRequest)?.Content??"";if(!ActionQuestions.IsActionQuestion(question))return false;
        var packet=new List<Message>{new("system",AgentProtocol.Instructions+ActionQuestions.Instructions+"This is a question, not permission to perform actions. Return only action reply with the answer in value. Do not request any tool. Infinity supplies confirmation buttons."),new("user",JsonSerializer.Serialize(new{project=AgentProject(task),connectedFolders=AgentRoots(task)}))};packet.AddRange(messages);if(ProjectGuidance.Message(AgentRoots(task)) is {} guidance)packet.Insert(1,guidance);if(SkillLibrary.ForRequest(AgentRoots(task),question) is {} skill)packet.Insert(1,skill);
        var projected=AgentContext.ForModel(packet,Agent.Approvals.Where(a=>a.ProjectId==AgentProject(task)?.Id));var output=forceCloud?await AgentGenerate(profileId,projected,token):await AgentGenerateWithRouting(profileId,projected,token);var call=AgentProtocol.Parse(output);
        var reply=call.Action=="reply"?call.Value:"I can help carry out the supported steps. Nothing has been performed; please confirm before I start.";
        var offer=ActionQuestions.Offer(question,reply,AgentOfferScope(task));if(offer!=null){offer.AgentConversation=task==null;offer.AgentTaskId=task?.Id;offer.ModelId=profileId;}
        messages.Add(new("assistant",JsonSerializer.Serialize(new{action="reply",value=reply}),Suggestion:offer));
        if(task!=null)
        {
            if(task.Goal!=null)task.Goal.State="Paused";task.Status="Response ready";task.Detail=reply;var chat=conversations.Single(c=>c.Id==task.ChatId);store.EnsureCurrent(chat);chat.Messages.Add(new("assistant",reply,profileId,Suggestion:offer));store.Save(chat);if(conversation.Id==chat.Id)Render();
        }
        SaveAgent();return true;
    }
    internal async Task AcceptAgentActionOffer(ActionSuggestion offer)
    {
        try
        {
            var task=offer.AgentTaskId is Guid id?Agent.Tasks.SingleOrDefault(t=>t.Id==id)??throw new IOException("Task unavailable."):null;
            var message=(task?.Messages??Agent.Messages).LastOrDefault();
            if(busy||AgentIsWorking||agentReviewDepth>0||message?.Suggestion is not {State:"Offered"} current||current.Id!=offer.Id||offer.Scope!=AgentOfferScope(task))throw new IOException("This offer is no longer current, or work is still running. Ask again after current work stops.");
            if(task!=null){RequireTaskAvailable(task);if(TaskNeedsReview(task))throw new IOException("Resolve this task's pending reviews first.");}
            current.State="Accepted";offer.State="Accepted";try{SaveAgent();}catch{current.State="Offered";offer.State="Offered";throw;}
            SyncAgentOfferChat(task,current);
            var directive="Proceed with this action I accepted: "+offer.Task+"\nDone when: "+offer.DoneWhen+"\nKeep every file, command and cloud review. Ask for missing details; do not invent successful actions.";
            if(task==null)await AgentSend(directive,offer.ModelId);
            else
            {
                var prior=task.Goal==null?null:JsonSerializer.Deserialize<AgentGoal>(JsonSerializer.Serialize(task.Goal));
                if(task.Goal is {} goal){goal.Objective=offer.Task;goal.SuccessCriteria=offer.DoneWhen;goal.State="Active";goal.TurnsUsed=0;goal.IdleTurns=0;goal.ProgressHash=AgentGoal.Fingerprint(task);}
                try{AgentFollowUp(task,directive);}catch{task.Goal=prior;throw;}
            }
            Render();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text="Action has not started: "+e.Message;agentView?.Refresh();}
    }
    private void SyncAgentOfferChat(AgentTask? task,ActionSuggestion offer)
    {
        if(task==null)return;var chat=conversations.Single(c=>c.Id==task.ChatId);var copy=chat.Messages.Select(m=>m.Suggestion).FirstOrDefault(s=>s?.Id==offer.Id);if(copy==null)return;store.EnsureCurrent(chat);copy.State=offer.State;store.Save(chat);
    }
    internal void DismissAgentActionOffer(ActionSuggestion offer)
    {
        try
        {
            var messages=offer.AgentTaskId is Guid id?Agent.Tasks.Single(t=>t.Id==id).Messages:Agent.Messages;var current=messages.LastOrDefault()?.Suggestion;
            if(AgentIsWorking||current?.Id!=offer.Id||current.State!="Offered")throw new IOException("This offer is no longer current.");current.State="Dismissed";offer.State="Dismissed";try{SaveAgent();}catch{current.State="Offered";offer.State="Offered";throw;}SyncAgentOfferChat(offer.AgentTaskId is Guid taskId?Agent.Tasks.Single(t=>t.Id==taskId):null,current);Render();
        }
        catch(IOException e){status.Text=e.Message;}
    }
}
internal sealed partial class AgentView
{
    private readonly StackPanel actionOfferPanel=new();
    private void RefreshActionOffer()
    {
        actionOfferPanel.Children.Clear();var messages=state.Tasks.SingleOrDefault(t=>t.Id==state.DraftTaskId)?.Messages??state.Messages;
        if(messages.LastOrDefault()?.Suggestion is not {State:"Offered"} offer)return;
        actionOfferPanel.Children.Add(new TextBlock{Text="Would you like me to do that?",FontWeight=System.Windows.FontWeights.SemiBold});actionOfferPanel.Children.Add(new TextBlock{Text=offer.Task,TextWrapping=System.Windows.TextWrapping.Wrap});var actions=new WrapPanel();actionOfferPanel.Children.Add(actions);var yes=new Button{Content="Yes, do it",IsEnabled=!host.AgentIsWorking};yes.Click+=async(_,_)=>await host.AcceptAgentActionOffer(offer);actions.Children.Add(yes);var no=new Button{Content="Not now",IsEnabled=!host.AgentIsWorking};no.Click+=(_,_)=>host.DismissAgentActionOffer(offer);actions.Children.Add(no);
    }
}
