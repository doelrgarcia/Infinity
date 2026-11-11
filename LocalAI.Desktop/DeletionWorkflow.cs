using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private Window? deletedItemsWindow;
    private IEnumerable<ProjectMemory> VisibleProjects=>Agent.Projects.Where(p=>p.DeletedAt==null);
    private bool IsChatDeleted(Conversation chat)=>chat.DeletedAt!=null||chat.ProjectId is Guid id&&agentState?.Projects.Any(p=>p.Id==id&&p.DeletedAt!=null&&p.DeleteChats)==true;
    private IEnumerable<Conversation> VisibleChats=>conversations.Where(c=>!IsChatDeleted(c));
    private void RequireTaskAvailable(AgentTask task)
    {
        if(task.ChatId is Guid chatId&&conversations.SingleOrDefault(c=>c.Id==chatId) is {} chat&&IsChatDeleted(chat)||task.ProjectId is Guid projectId&&Agent.Projects.Any(p=>p.Id==projectId&&p.DeletedAt!=null))throw new IOException("This task belongs to a deleted chat or project. Restore it from Deleted items before continuing.");
    }
    private ContextMenu DeleteMenu(string label,Action action)
    {
        return Disclosure("","",(label,action)).ContextMenu;
    }
    private void InstallChatActions()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));var more=new FrameworkElementFactory(typeof(Button));more.SetValue(ContentControl.ContentProperty,"⋯");more.SetValue(DockPanel.DockProperty,Dock.Right);more.SetValue(Control.PaddingProperty,new Thickness(6,0,6,0));more.SetValue(FrameworkElement.MarginProperty,new Thickness(3,0,0,0));more.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("Title"){StringFormat="Chat actions: {0}"});
        more.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler((sender,e)=>{e.Handled=true;if(sender is Button {DataContext:Conversation chat} button){button.ContextMenu=ChatMenu(chat);button.ContextMenu.PlacementTarget=button;button.ContextMenu.IsOpen=true;}}));row.AppendChild(more);
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("Title"));title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);title.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(title);history.ItemTemplate=new DataTemplate{VisualTree=row};
        history.ContextMenu=new ContextMenu();history.ContextMenuOpening+=(_,e)=>{var container=ItemsControl.ContainerFromElement(history,e.OriginalSource as DependencyObject) as ListBoxItem;var chat=container?.Content as Conversation??history.SelectedItem as Conversation;if(chat==null){e.Handled=true;return;}history.ContextMenu=ChatMenu(chat);};
    }
    private void RequireDeletionIdle(IEnumerable<Conversation> chats,Guid? project=null)
    {
        if(busy||dictation!=null||chatRequestDepth>0||AgentIsWorking||agentReviewDepth>0||Application.Current.Windows.OfType<Window>().Any(w=>w.Owner==this&&w.IsVisible&&w is not BrowserWindow&&w!=deletedItemsWindow))throw new IOException("Finish or stop current work and close its review before deleting or restoring items.");var ids=chats.Select(c=>c.Id).ToHashSet();var tasks=Agent.Tasks.Where(t=>t.ChatId.HasValue&&ids.Contains(t.ChatId.Value)||project.HasValue&&t.ProjectId==project).ToArray();
        if(tasks.Any(t=>t.Status is "Running" or "Queued"))throw new IOException("Stop the related tasks before deleting this item.");
        if(Agent.Approvals.Any(a=>(a.TaskId.HasValue&&tasks.Any(t=>t.Id==a.TaskId)||project.HasValue&&a.ProjectId==project)&&AgentPresentation.VisibleReview(a)))throw new IOException("Resolve the related pending or uncertain reviews before deleting this item.");
    }
    private void DeleteChat(Conversation chat)
    {
        try
        {
            EnsureAgentState();RequireDeletionIdle([chat]);if(IsChatDeleted(chat))throw new IOException("Chat is already deleted.");
            var dialog=new DeleteItemWindow("Delete chat",chat.Title,"This chat will leave your history and move to Deleted items, where you can restore it. Local files, project folders and task audit receipts are preserved."){Owner=this};if(dialog.ShowDialog()!=true)return;
            SetChatDeleted(chat,true);status.Text="Chat deleted. Restore it under Tools > Deleted items.";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Chat not deleted: "+e.Message;}
    }
    private void SetChatDeleted(Conversation chat,bool deleted)
    {
        RequireDeletionIdle([chat]);store.EnsureCurrent(chat);var previous=chat.DeletedAt;chat.DeletedAt=deleted?DateTimeOffset.UtcNow:null;try{store.Save(chat);}catch{chat.DeletedAt=previous;throw;}RefreshDeletedNavigation();
    }
    private void DeleteProject(ProjectMemory project)
    {
        try
        {
            EnsureAgentState();var chats=conversations.Where(c=>c.ProjectId==project.Id).ToArray();RequireDeletionIdle(chats,project.Id);if(project.DeletedAt!=null)throw new IOException("Project is already deleted.");
            var dialog=new DeleteItemWindow("Delete project",project.Name,"Project files and isolated Git workspaces stay on disk. Deleted items remain locally recoverable. Related task history is preserved and cannot run while its project is deleted.",chats.Count(c=>c.DeletedAt==null)){Owner=this};if(dialog.ShowDialog()!=true)return;
            SetProjectDeleted(project,true,dialog.DeleteChats);status.Text=dialog.DeleteChats?"Project and its chats deleted. Restore under Tools > Deleted items.":"Project deleted. Its chats remain available under All chats.";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Project not deleted: "+e.Message;}
    }
    private void SetProjectDeleted(ProjectMemory project,bool deleted,bool deleteChats=true)
    {
        RequireDeletionIdle(conversations.Where(c=>c.ProjectId==project.Id),project.Id);if(!deleted&&VisibleProjects.Any(p=>p.Id!=project.Id&&string.Equals(p.Name,project.Name,StringComparison.OrdinalIgnoreCase)))throw new IOException("A current project has this name. Rename it before restoring this project.");var previous=project.DeletedAt;var previousChats=project.DeleteChats;var active=Agent.ActiveProjectId;
        if(deleted){project.DeletedAt=DateTimeOffset.UtcNow;project.DeleteChats=deleteChats;if(Agent.ActiveProjectId==project.Id)Agent.ActiveProjectId=null;}else project.DeletedAt=null;
        try{SaveAgent();}catch{project.DeletedAt=previous;project.DeleteChats=previousChats;Agent.ActiveProjectId=active;throw;}RefreshDeletedNavigation();
    }
    private void RefreshDeletedNavigation()
    {
        if(chatProjectFilter is Guid id&&!VisibleProjects.Any(p=>p.Id==id))chatProjectFilter=null;
        CollectionViewSource.GetDefaultView(conversations).Refresh();RefreshProjectNavigation();
        if(IsChatDeleted(conversation)){var next=VisibleChats.FirstOrDefault(c=>!chatProjectFilter.HasValue||c.ProjectId==chatProjectFilter);if(next!=null)history.SelectedItem=next;else NewConversation();}else Render();
        UpdateChatAssistantBadge();
    }
    private void OpenDeletedItems()
    {
        try
        {
            RequireProjectNavigationIdle();EnsureAgentState();var dialog=new Window{Owner=this,Title="Deleted items",Width=650,Height=460,WindowStartupLocation=WindowStartupLocation.CenterOwner};DarkTheme.Apply(dialog);var body=new DockPanel{Margin=new Thickness(24)};DarkTheme.Wrap(dialog,body);
            var help=new TextBlock{Text="Deleted chats and projects are retained locally until restored. Restoring never starts tasks or runs pending follow-ups. Restore a project first to recover chats deleted with it.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(help,Dock.Top);body.Children.Add(help);var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);var restore=new Button{Content="Restore selected",Margin=new Thickness(0,10,8,0)};actions.Children.Add(restore);var close=new Button{Content="Close",Margin=new Thickness(0,10,0,0),IsCancel=true};actions.Children.Add(close);var list=new ListBox();body.Children.Add(list);
            void Refresh(){list.Items.Clear();foreach(var project in Agent.Projects.Where(p=>p.DeletedAt!=null))list.Items.Add(new ListBoxItem{Content="Project · "+project.Name,Tag=project});foreach(var chat in conversations.Where(c=>c.DeletedAt!=null))list.Items.Add(new ListBoxItem{Content="Chat · "+chat.Title,Tag=chat});restore.IsEnabled=list.Items.Count>0;}
            restore.Click+=(_,_)=>{try{if(list.SelectedItem is ListBoxItem {Tag:ProjectMemory project})SetProjectDeleted(project,false);else if(list.SelectedItem is ListBoxItem {Tag:Conversation chat}){if(chat.ProjectId is Guid id&&Agent.Projects.Any(p=>p.Id==id&&p.DeletedAt!=null&&p.DeleteChats))throw new IOException("Restore this chat's project first.");SetChatDeleted(chat,false);}Refresh();}catch(Exception e){help.Text=e.Message;}};Refresh();deletedItemsWindow=dialog;try{dialog.ShowDialog();}finally{deletedItemsWindow=null;}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;}
    }
}
internal sealed class DeleteItemWindow:Window
{
    private readonly CheckBox chats=new(){IsChecked=true,Margin=new Thickness(0,12,0,12)};
    internal bool DeleteChats=>chats.IsChecked==true;
    internal DeleteItemWindow(string title,string name,string explanation,int? count=null)
    {
        Title=title;Width=540;Height=Math.Min(440,SystemParameters.WorkArea.Height-24);WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);var body=new DockPanel{Margin=new Thickness(24)};DarkTheme.Wrap(this,body);var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);var delete=new Button{Content=title};delete.Click+=(_,_)=>DialogResult=true;actions.Children.Add(delete);var content=new StackPanel();body.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});content.Children.Add(new TextBlock{Text=title+" “"+name+"”?",TextWrapping=TextWrapping.Wrap,FontSize=22});content.Children.Add(new TextBlock{Text=explanation,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,0)});if(count.HasValue){chats.Content="Also delete all "+count.Value+" chats in this project";content.Children.Add(chats);}
    }
}
