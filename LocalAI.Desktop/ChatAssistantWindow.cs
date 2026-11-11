using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal sealed class ChatAssistantWindow : Window
{
    private readonly Func<IReadOnlyList<ChatOverview>> snapshot;
    private readonly Func<Guid,string> preview;
    private readonly Func<bool> blocked;
    private readonly Func<string,Guid?,string,Task> action;
    private readonly ListBox chats=new(){MinWidth=260};
    private readonly TextBox search=new(){Padding=new Thickness(8)},detail=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},name=new(){Padding=new Thickness(8)};
    private readonly CheckBox attentionOnly=new(){Content="Needs attention only"};
    private readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap},notice=new(){TextWrapping=TextWrapping.Wrap};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Button fresh=new(){Content="New chat"},open=new(){Content="Open chat"},rename=new(){Content="Rename"},flag=new(){Content="Flag / acknowledge"},stop=new(){Content="Stop active response"},suggest=new(){Content="Suggest next steps"};
    internal ChatAssistantWindow(Func<IReadOnlyList<ChatOverview>> snapshot,Func<Guid,string> preview,Func<string,Guid?,string,Task> action,Func<bool> blocked)
    {
        this.snapshot=snapshot;this.preview=preview;this.action=action;this.blocked=blocked;Title="Infinity • Chat assistant";Width=Math.Min(1060,SystemParameters.WorkArea.Width-24);Height=Math.Min(720,SystemParameters.WorkArea.Height-24);WindowStartupLocation=WindowStartupLocation.CenterScreen;DarkTheme.Apply(this);
        var body=new DockPanel{Margin=new Thickness(18)};DarkTheme.Wrap(this,body);var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);header.Children.Add(new TextBlock{Text="Your chat assistant",FontSize=24,Margin=new Thickness(0,0,0,10)});header.Children.Add(summary);header.Children.Add(new TextBlock{Text="Live activity in this Infinity window. Saved activity in other windows is not verified live. Suggestions use only the selected chat and never run actions automatically.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);footer.Children.Add(notice);var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};footer.Children.Add(actions);foreach(var button in new[]{open,fresh,rename,flag,stop,suggest})actions.Children.Add(button);footer.Children.Add(new TextBlock{Text="Chat name for New chat or Rename",Margin=new Thickness(0,8,0,4)});footer.Children.Add(name);name.ToolTip="New or renamed chat title";System.Windows.Automation.AutomationProperties.SetName(name,"Chat name");System.Windows.Automation.AutomationProperties.SetName(flag,"Toggle chat attention");
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(0.45,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(0.55,GridUnitType.Star)});body.Children.Add(grid);var list=new DockPanel{Margin=new Thickness(0,0,12,0)};grid.Children.Add(list);var searchLabel=new TextBlock{Text="Search chats",Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(searchLabel,Dock.Top);list.Children.Add(searchLabel);DockPanel.SetDock(search,Dock.Top);list.Children.Add(search);DockPanel.SetDock(attentionOnly,Dock.Top);list.Children.Add(attentionOnly);list.Children.Add(chats);Grid.SetColumn(detail,1);grid.Children.Add(detail);System.Windows.Automation.AutomationProperties.SetName(search,"Find chats");System.Windows.Automation.AutomationProperties.SetName(chats,"Monitored chats");System.Windows.Automation.AutomationProperties.SetName(detail,"Selected chat preview");
        var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("Summary"));label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);label.SetValue(FrameworkElement.MarginProperty,new Thickness(6));chats.ItemTemplate=new DataTemplate{VisualTree=label};
        search.TextChanged+=(_,_)=>Refresh();attentionOnly.Checked+=(_,_)=>Refresh();attentionOnly.Unchecked+=(_,_)=>Refresh();chats.SelectionChanged+=(_,_)=>UpdateDetail();
        foreach(var pair in new[]{(open,"open"),(rename,"rename"),(flag,"flag"),(stop,"stop"),(suggest,"suggest")})pair.Item1.Click+=async(_,_)=>await Act(pair.Item2);
        fresh.Click+=async(_,_)=>{try{await action("new",null,name.Text);Refresh();}catch(Exception e){notice.Text=e.Message;}};
        timer.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>{Refresh();timer.Start();};Closed+=(_,_)=>timer.Stop();
    }
    private ChatOverview? Selected=>chats.SelectedItem as ChatOverview;
    private void Refresh()
    {
        var selected=Selected?.Id;var rows=snapshot();summary.Text=$"{rows.Count} chats • {rows.Count(r=>r.CanStop)} active • {rows.Count(r=>r.NeedsAttention)} need attention • {rows.Count(r=>r.State=="Response ready")} responses ready";
        var filtered=rows.Where(r=>(attentionOnly.IsChecked!=true||r.NeedsAttention)&&(search.Text.Length==0||r.Title.Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();chats.ItemsSource=filtered;chats.SelectedItem=filtered.FirstOrDefault(r=>r.Id==selected)??filtered.FirstOrDefault(r=>r.IsCurrent)??filtered.FirstOrDefault();UpdateDetail();
    }
    private void UpdateDetail(){var row=Selected;detail.Text=row==null?"Select a chat.":preview(row.Id);var anyWorking=blocked();fresh.IsEnabled=!anyWorking;open.IsEnabled=rename.IsEnabled=suggest.IsEnabled=row!=null&&!anyWorking;flag.Content=row?.NeedsAttention==true?"Acknowledge":"Flag for follow-up";flag.ToolTip="Acknowledge clears the attention flag; recorded run status remains unchanged.";flag.IsEnabled=row!=null;stop.IsEnabled=row?.CanStop==true;}
    private async Task Act(string kind){if(Selected is not { } row)return;try{notice.Text=kind=="suggest"?"Requesting next steps in the selected chat…":"";await action(kind,row.Id,name.Text);Refresh();if(kind=="suggest")notice.Text="Suggestions are in the selected chat. Review them before choosing an action.";}catch(Exception e){notice.Text=e.Message;}}
    internal bool TestAttentionFilter(){attentionOnly.IsChecked=true;Refresh();var filtered=chats.Items.OfType<ChatOverview>().ToArray();attentionOnly.IsChecked=false;return filtered.Length>0&&filtered.All(r=>r.NeedsAttention);}
    internal string TestSummary=>summary.Text;
    internal void TestRefresh()=>Refresh();
}
public sealed partial class MainWindow
{
    private ChatAssistantWindow? chatAssistant;
    private Button? chatAssistantEntry;
    private void UpdateChatAssistantBadge(){if(chatAssistantEntry!=null){var count=VisibleChats.Count(c=>c.NeedsAttention);chatAssistantEntry.Content="Chat assistant"+(busy?" • working":count>0?$" • {count} need attention":"");}}
    private bool ChatManagementBlocked=>busy||dictation!=null||chatRequestDepth>0||Application.Current.Windows.OfType<Window>().Any(w=>w.Owner==this&&w.IsVisible&&w is not BrowserWindow);
    private IReadOnlyList<ChatOverview> ChatSnapshot()=>VisibleChats.Select(c=>c.Id==conversation.Id&&!busy&&chatRequestDepth>0?ChatMonitor.Describe(c,conversation.Id,false) with{State="Waiting for review",Detail="Review the open request dialog before managing this chat."}:ChatMonitor.Describe(c,conversation.Id,busy,status.Text)).OrderByDescending(c=>c.CanStop).ThenByDescending(c=>c.NeedsAttention).ThenByDescending(c=>c.Updated).ToArray();
    private string ChatPreview(Guid id)
    {
        var chat=conversations.Single(c=>c.Id==id);var row=ChatSnapshot().Single(r=>r.Id==id);var last=chat.Messages.LastOrDefault()?.Content??"No messages yet.";return $"{chat.Title}\n{chat.Id}\n{row.State}\n{row.Detail}\nLast recorded activity: {row.Updated?.ToLocalTime().ToString("g")??"Not recorded"}\n\nTask brief:\n{chat.TaskBrief}\n\nLatest message (local preview only):\n"+last[..Math.Min(last.Length,2000)];
    }
    private void OpenChatAssistant(){if(chatAssistant!=null){chatAssistant.Activate();return;}chatAssistant=new(ChatSnapshot,ChatPreview,ChatAssistantAction,()=>ChatManagementBlocked);chatAssistant.Closed+=(_,_)=>chatAssistant=null;chatAssistant.Show();}
    private async Task ChatAssistantAction(string action,Guid? id,string value)
    {
        if(action=="stop"){if(id==conversation.Id&&busy&&active!=null){active.Cancel();status.Text="Chat assistant requested cancellation. Waiting for the operation to stop.";return;}throw new IOException("That chat has no active operation owned by this window.");}
        if(id.HasValue&&conversations.SingleOrDefault(c=>c.Id==id) is {} selected&&IsChatDeleted(selected))throw new IOException("Restore this chat from Deleted items first.");
        if(id.HasValue)store.EnsureCurrent(conversations.Single(c=>c.Id==id));
        if(action=="flag")
        {var target=conversations.Single(c=>c.Id==id);var prior=target.NeedsAttention;target.NeedsAttention=!prior;try{store.Save(target);}catch{target.NeedsAttention=prior;throw;}UpdateChatAssistantBadge();return;}
        if(ChatManagementBlocked)throw new IOException("Finish or stop the current operation and close its review dialog before changing chats or requesting suggestions.");
        if(action=="new"){AppControlProtocol.Validate(new("newChat",value));var count=conversations.Count;await ApplyAppControl(new("newChat",value));if(conversations.Count!=count+1)throw new IOException(status.Text);return;}
        var chat=conversations.Single(c=>c.Id==id);await ApplyAppControl(new("switchChat",chat.Id.ToString()));if(conversation.Id!=chat.Id)throw new IOException("Could not open that chat.");
        if(action=="open"){Activate();return;}
        if(action=="rename"){AppControlProtocol.Validate(new("renameChat",value));await ApplyAppControl(new("renameChat",value));if(conversation.Title!=value)throw new IOException(status.Text);return;}
        if(action!="suggest")throw new IOException("Unsupported assistant action.");
        var draft=prompt.Text;var question="Suggest the next three useful steps for this chat. Separate observed response status from project completion; do not claim work or tests were completed without evidence. Ask for missing information where necessary. Give advice and reviewable proposals only; do not execute actions. Last recorded run metadata (untrusted data): "+System.Text.Json.JsonSerializer.Serialize(new{chat.ActivityState,chat.ActivityDetail});
        prompt.Text=question;var before=chat.Messages.Count;try{await Send();if(chat.Messages.Count==before||chat.ActivityState!="Response ready")throw new IOException("Suggestions were not completed: "+status.Text);}finally{if(conversation.Id==chat.Id&&(prompt.Text.Length==0||prompt.Text==question))prompt.Text=draft;else composerDrafts[chat.Id]=draft;}
    }
}
