using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private string ActionScope()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{conversation.Id,conversation.ProjectId,conversation.WorkspaceRoots,chatProjectFilter}))));
    private Task<bool> OfferLocalAppQuestion(string text)
    {
        if(DraftImages.Count>0||DraftFiles.Count>0||!ActionQuestions.IsActionQuestion(text))return Task.FromResult(false);
        var action=AppControlProtocol.FromPrompt(text);var phrasing=text.Split(['\r','\n'],2)[0];var workflow="";
        if(!System.Text.RegularExpressions.Regex.IsMatch(phrasing,@"\b(?:ability|feature|implement|support)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var deletion=System.Text.RegularExpressions.Regex.Match(phrasing,@"\b(?:delete|remove)\s+(?:(?:this|a|the|my|current|all)\s+)?(?<kind>chat|conversation|project)s?\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(deletion.Success)workflow=deletion.Groups["kind"].Value.Equals("project",StringComparison.OrdinalIgnoreCase)?"Delete project":"Delete chat";
            else if(System.Text.RegularExpressions.Regex.IsMatch(phrasing,@"\b(?:create|start|make)\s+(?:a |new |a new )?project\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))workflow="New project";
        }
        if(action==null&&workflow.Length==0)return Task.FromResult(false);
        var names=new Dictionary<string,string>{{"newChat","Create a new chat"},{"listChats","List your chats"},{"switchChat","Open the selected chat"},{"renameChat","Rename this chat"},{"showView","Open this Infinity view"},{"setResponseStyle","Change the response style"},{"selectModel","Choose this model"},{"openBrowser","Open the browser"},{"browserNavigate","Navigate the browser"},{"browserLink","Follow this browser link"},{"debugProject","Open the reviewed debug workflow"},{"chatAssistant","Open chat activity"},{"chatStatus","Show chat status"},{"attentionChats","Show chats needing attention"}};
        var offer=new ActionSuggestion{Task=action==null?(workflow=="New project"?"Open the new project form":"Choose which "+(workflow=="Delete chat"?"chat":"project")+" to delete, then review its deletion confirmation"):(names.GetValueOrDefault(action.Action,"Open the requested Infinity workflow")+(action.Value.Length==0?"":": "+action.Value)),DoneWhen="The requested Infinity workflow is opened or the action is confirmed.",Scope=ActionScope(),AppAction=action,LocalWorkflow=workflow};
        var question=new Message("user",text);var answer=new Message("assistant","I can help with that using Infinity's existing controls. Nothing has been performed.",Suggestion:offer);store.EnsureCurrent(conversation);conversation.Messages.Add(question);conversation.Messages.Add(answer);
        try{store.Save(conversation);}catch{conversation.Messages.Remove(answer);conversation.Messages.Remove(question);throw;}
        prompt.Clear();Render();status.Text="Action offered. Nothing has been performed.";return Task.FromResult(true);
    }
    private ActionSuggestion? CurrentActionOffer=>conversation.Messages.LastOrDefault()?.Suggestion is {State:"Offered"} offer?offer:null;
    private async Task<bool> AnswerActionOffer(string text)
    {
        if(CurrentActionOffer is not {} offer)return false;var reply=text.Trim().TrimEnd('.','!').ToLowerInvariant();
        if(reply is "yes" or "yes please" or "yes, please" or "do it" or "go ahead" or "yes do it" or "yes, do it"){await AcceptActionOffer(conversation.Id,offer);return true;}
        if(reply is "no" or "no thanks" or "no, thanks" or "not now"){DismissActionOffer(conversation.Id,offer);return true;}return false;
    }
    private void RenderActionOffer(Panel parent,Message message)
    {
        if(message.Suggestion is not {} offer)return;var card=new StackPanel{Margin=new Thickness(0,12,0,0)};
        card.Children.Add(new TextBlock{Text="Would you like me to do that?",FontWeight=FontWeights.SemiBold});card.Children.Add(new TextBlock{Text=offer.Task,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});card.Children.Add(new TextBlock{Text="Changes, commands and cloud requests still need their own review.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});
        var actions=new WrapPanel();card.Children.Add(actions);var id=conversation.Id;var current=ReferenceEquals(CurrentActionOffer,offer)&&offer.State=="Offered";
        var yes=new Button{Content="Yes, do it",IsEnabled=current,Margin=new Thickness(0,0,8,0)};yes.Click+=async(_,_)=>await AcceptActionOffer(id,offer);actions.Children.Add(yes);var no=new Button{Content="Not now",IsEnabled=current};no.Click+=(_,_)=>DismissActionOffer(id,offer);actions.Children.Add(no);
        if(!current)card.Children.Add(new TextBlock{Text=offer.State=="Offered"?"Earlier offer — ask again to continue.":offer.State,FontSize=11});parent.Children.Add(card);
    }
    private void CheckActionOffer(Guid chat,ActionSuggestion offer)
    {
        if(chat!=conversation.Id||IsChatDeleted(conversation)||!ReferenceEquals(CurrentActionOffer,offer)||offer.Scope!=ActionScope())throw new IOException("This offer is no longer current. Ask again before starting the action.");
        if(busy||AgentIsWorking||agentReviewDepth>0||ChatManagementBlocked)throw new IOException("Finish or stop the current work and close its review first. The offer is retained.");offer.Validate();store.EnsureCurrent(conversation);if(offer.AppAction==null&&offer.LocalWorkflow.Length==0&&Agent.Tasks.LastOrDefault(t=>t.ChatId==conversation.Id) is {} task){RequireTaskAvailable(task);if(TaskNeedsReview(task))throw new IOException("Resolve this task's pending reviews first. The offer is retained.");}
    }
    private async Task AcceptActionOffer(Guid chat,ActionSuggestion offer)
    {
        if(chat!=conversation.Id){status.Text="Open the chat containing this offer before accepting it.";return;}
        if(offer.AgentConversation||offer.AgentTaskId.HasValue){await AcceptAgentActionOffer(offer);return;}
        try
        {
            CheckActionOffer(chat,offer);offer.State="Accepted";try{store.Save(conversation);}catch{offer.State="Offered";throw;}
            // The visible offer authorizes starting this exact workflow, not any tool approval.
            if(offer.AppAction is {} action)await ApplyAppControl(action);
            else if(offer.LocalWorkflow.Length>0)OpenSuggestedWorkflow(offer.LocalWorkflow);
            else
            {
                var draftImages=DraftImages.ToArray();var draftFiles=DraftFiles.ToArray();DraftImages.Clear();DraftFiles.Clear();
                try{StartChatGoal(new(){Objective="User accepted this action: "+offer.Task,SuccessCriteria=offer.DoneWhen,CompletionMode="User acceptance"});}
                finally{fileDrafts[chat]=draftFiles.ToList();imageDrafts[chat]=draftImages.ToList();if(conversation.Id==chat)RefreshImageTray();}
            }
            if(prompt.Text.Trim().ToLowerInvariant() is "yes" or "yes please" or "yes, please" or "do it" or "go ahead" or "yes do it" or "yes, do it")prompt.Clear();Render();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text="Action has not started: "+e.Message;Render();}
    }
    private void OpenSuggestedWorkflow(string workflow)
    {
        if(workflow=="New project"){OpenNewProject();return;}
        var dialog=new Window{Owner=this,Title="Choose what to delete",Width=550,Height=430,WindowStartupLocation=WindowStartupLocation.CenterOwner};DarkTheme.Apply(dialog);var body=new DockPanel{Margin=new Thickness(24)};DarkTheme.Wrap(dialog,body);var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);var review=new Button{Content="Review deletion"};actions.Children.Add(review);actions.Children.Add(new Button{Content="Cancel",IsCancel=true});var list=new ListBox();body.Children.Add(list);
        if(workflow=="Delete chat")foreach(var chat in VisibleChats)list.Items.Add(new ListBoxItem{Content=chat.Title,Tag=chat});else foreach(var project in VisibleProjects)list.Items.Add(new ListBoxItem{Content=project.Name,Tag=project});review.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};
        if(dialog.ShowDialog()==true&&list.SelectedItem is ListBoxItem chosen){if(chosen.Tag is Conversation chat)DeleteChat(chat);else if(chosen.Tag is ProjectMemory project)DeleteProject(project);}
    }
    private void DismissActionOffer(Guid chat,ActionSuggestion offer)
    {
        if(chat!=conversation.Id){status.Text="Open the chat containing this offer first.";return;}
        if(offer.AgentConversation||offer.AgentTaskId.HasValue){DismissAgentActionOffer(offer);return;}
        try{CheckActionOffer(chat,offer);offer.State="Dismissed";try{store.Save(conversation);}catch{offer.State="Offered";throw;}if(prompt.Text.Trim().ToLowerInvariant() is "no" or "no thanks" or "no, thanks" or "not now")prompt.Clear();Render();status.Text="No action started.";}
        catch(IOException e){status.Text=e.Message;}
    }
}
