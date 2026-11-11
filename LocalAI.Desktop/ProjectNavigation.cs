using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private readonly StackPanel projectRows=new();
    private readonly ScrollViewer projectChatScroll=new(){MaxHeight=340,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly HashSet<Guid> expandedChatProjects=[];
    private readonly TextBlock recentChatsHeading=new(){Text="RECENT CHATS",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=Brush("#7F8A9D"),Margin=new Thickness(8,0,0,8)};
    private Guid? chatProjectFilter;
    private Action<NewProjectWindow>? projectDialogTest;

    private void BuildProjectNavigation(Panel navigation)
    {
        var create=Button("New project",(_,_)=>OpenNewProject());create.HorizontalContentAlignment=HorizontalAlignment.Left;navigation.Children.Add(create);
        navigation.Children.Add(new TextBlock{Text="PROJECTS",FontSize=10,Margin=new Thickness(8,10,0,4),Foreground=Brush("#98A4B7")});
        projectChatScroll.Content=projectRows;navigation.Children.Add(projectChatScroll);
        void FitProjectChats()=>projectChatScroll.MaxHeight=Math.Clamp(ActualHeight-380,120,340);
        Loaded+=(_,_)=>FitProjectChats();SizeChanged+=(_,_)=>FitProjectChats();
        RefreshProjectNavigation();
    }
    private void RefreshProjectNavigation()
    {
        projectRows.Children.Clear();
        try
        {
            EnsureAgentState();
            history.Visibility=recentChatsHeading.Visibility=chatProjectFilter.HasValue?Visibility.Collapsed:Visibility.Visible;
            FrameworkElement? selectedRow=null;
            foreach(var project in VisibleProjects.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase))
            {
                var projectChats=VisibleChats.Where(c=>c.ProjectId==project.Id).ToArray();
                var searching=!string.IsNullOrWhiteSpace(historySearch.Text);
                bool Matches(Conversation chat)=>!searching||chat.Title.Contains(historySearch.Text,StringComparison.OrdinalIgnoreCase)||chat.Messages.Any(m=>m.Content.Contains(historySearch.Text,StringComparison.OrdinalIgnoreCase));
                if(searching&&!project.Name.Contains(historySearch.Text,StringComparison.OrdinalIgnoreCase)&&!projectChats.Any(Matches))continue;
                var expanded=expandedChatProjects.Contains(project.Id)||searching;
                var button=FlatButton(project.Name,()=>OpenProjectChats(project.Id),"Open project: "+project.Name);
                button.Content=new TextBlock{Text=project.Name,TextTrimming=TextTrimming.CharacterEllipsis};button.ToolTip=project.Name;
                button.HorizontalContentAlignment=HorizontalAlignment.Left;button.HorizontalAlignment=HorizontalAlignment.Stretch;
                button.Background=Brush(chatProjectFilter==project.Id?"#353535":"#171717");var row=new DockPanel();var more=FlatButton("⋯",()=>{},"Project actions: "+project.Name);more.ContextMenu=DeleteMenu("Delete project",()=>DeleteProject(project));more.Click+=(_,_)=>{more.ContextMenu.PlacementTarget=more;more.ContextMenu.IsOpen=true;};DockPanel.SetDock(more,Dock.Right);row.Children.Add(more);
                var toggle=FlatButton(expanded?"▾":"▸",()=>{if(!expandedChatProjects.Remove(project.Id))expandedChatProjects.Add(project.Id);RefreshProjectNavigation();},(expanded?"Collapse":"Expand")+" chats in project: "+project.Name);toggle.Padding=new Thickness(4,6,4,6);toggle.Margin=new Thickness(0);DockPanel.SetDock(toggle,Dock.Left);row.Children.Add(toggle);
                row.Children.Add(button);button.ContextMenu=DeleteMenu("Delete project",()=>DeleteProject(project));projectRows.Children.Add(row);
                if(chatProjectFilter==project.Id)selectedRow=row;
                if(!expanded)continue;
                var children=new StackPanel{Margin=new Thickness(22,2,0,8),Tag=project.Id};projectRows.Children.Add(children);
                var newChat=FlatButton("+ New chat",()=>NewChatInProject(project.Id),"New chat in project: "+project.Name);newChat.HorizontalContentAlignment=HorizontalAlignment.Left;children.Children.Add(newChat);
                foreach(var chat in projectChats.Where(Matches))
                {
                    var chatRow=new DockPanel{Tag=chat.Id,DataContext=chat};
                    var chatMore=FlatButton("⋯",()=>{},"Chat actions: "+chat.Title);chatMore.ContextMenu=ChatMenu(chat);chatMore.Click+=(_,_)=>{chatMore.ContextMenu.PlacementTarget=chatMore;chatMore.ContextMenu.IsOpen=true;};DockPanel.SetDock(chatMore,Dock.Right);chatRow.Children.Add(chatMore);
                    var open=FlatButton(chat.Title,()=>OpenNestedProjectChat(project.Id,chat.Id),"Open chat: "+chat.Title+" in project: "+project.Name);open.Tag=chat.Id;open.DataContext=chat;open.Content=new TextBlock{Text=chat.Title,TextTrimming=TextTrimming.CharacterEllipsis};open.HorizontalContentAlignment=HorizontalAlignment.Left;open.Background=Brush(conversation.Id==chat.Id?"#353535":"#171717");open.ContextMenu=ChatMenu(chat);chatRow.Children.Add(open);children.Children.Add(chatRow);
                    if(conversation.Id==chat.Id&&chatProjectFilter==project.Id)selectedRow=chatRow;
                }
            }
            var all=FlatButton("All chats",()=>OpenProjectChats(null),"Show all chats");all.HorizontalContentAlignment=HorizontalAlignment.Left;projectRows.Children.Add(all);
            if(selectedRow is {} selected)
            {
                var selectedId=chatProjectFilter;
                Dispatcher.BeginInvoke(new Action(()=>
                {
                    if(chatProjectFilter==selectedId&&selected.IsDescendantOf(projectRows))selected.BringIntoView();
                }),System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {status.Text="Projects could not be loaded: "+e.Message;}
    }
    private void NewChatInProject(Guid id)
    {
        try
        {
            RequireProjectNavigationIdle();EnsureAgentState();if(!VisibleProjects.Any(p=>p.Id==id))throw new IOException("Project not found.");
            historySearch.Clear();chatProjectFilter=id;expandedChatProjects.Add(id);CollectionViewSource.GetDefaultView(conversations).Refresh();NewConversation();ShowChat();prompt.Focus();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Could not create project chat: "+e.Message;}
    }
    private void OpenNestedProjectChat(Guid projectId,Guid chatId)
    {
        try{RequireProjectNavigationIdle();var chat=VisibleChats.SingleOrDefault(c=>c.Id==chatId&&c.ProjectId==projectId)??throw new IOException("Chat not found in this project.");SelectSidebarChat(chat);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;}
    }
    private void SelectSidebarChat(Conversation chat)
    {
        EnsureAgentState();if(IsChatDeleted(chat))throw new IOException("Restore this chat before opening it.");
        historySearch.Clear();chatProjectFilter=chat.ProjectId is Guid id&&VisibleProjects.Any(p=>p.Id==id)?id:null;
        if(chatProjectFilter is Guid projectId)expandedChatProjects.Add(projectId);
        CollectionViewSource.GetDefaultView(conversations).Refresh();history.SelectedItem=chat;Render();ShowChat();status.Text="Chat: "+chat.Title;
    }
    private void RequireProjectNavigationIdle(Window? setupDialog=null)
    {
        var otherDialog=Application.Current.Windows.OfType<Window>().Any(w=>w.Owner==this&&w.IsVisible&&w is not BrowserWindow&&w!=setupDialog);
        if(busy||dictation!=null||chatRequestDepth>0||otherDialog||AgentIsWorking||agentReviewDepth>0)throw new IOException("Finish or stop the current operation and close its review before changing projects. Your draft is retained.");
    }
    private void OpenProjectChats(Guid? projectId)
    {
        try{SelectProjectChats(projectId);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;}
    }
    private void SelectProjectChats(Guid? projectId)
    {
        RequireProjectNavigationIdle();EnsureAgentState();
        if(projectId.HasValue&&!VisibleProjects.Any(p=>p.Id==projectId))throw new IOException("Project not found.");
        historySearch.Clear();chatProjectFilter=projectId;if(projectId.HasValue)expandedChatProjects.Add(projectId.Value);CollectionViewSource.GetDefaultView(conversations).Refresh();RefreshProjectNavigation();
        var target=VisibleChats.FirstOrDefault(c=>!projectId.HasValue||c.ProjectId==projectId);
        if(target!=null){history.SelectedItem=target;ShowChat();}else NewConversation();
        status.Text=projectId.HasValue?"Project: "+Agent.Projects.Single(p=>p.Id==projectId).Name:"All chats";prompt.Focus();
    }
    private void OpenNewProject()
    {
        try
        {
            RequireProjectNavigationIdle();
            EnsureAgentState();ProjectMemory? saved=null;NewProjectWindow? dialog=null;
            dialog=new NewProjectWindow(VisibleProjects.Select(p=>p.Name),Directory.Exists(@"C:\Projects\LocalAI")?@"C:\Projects\LocalAI":null,(title,path,notes)=>
            {
                RequireProjectNavigationIdle(dialog);
                saved=SaveChatProject(title,path,notes);
            }){Owner=this};if(projectDialogTest!=null)dialog.Loaded+=(_,_)=>projectDialogTest(dialog);
            if(dialog.ShowDialog()!=true)return;
            OpenCreatedProject(saved??throw new IOException("Project was not saved. Please try again."));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){status.Text="Could not finish project setup: "+e.Message;}
    }
    private ProjectMemory CreateChatProject(string name,string folder,string notes)
    {
        RequireProjectNavigationIdle();EnsureAgentState();
        var project=SaveChatProject(name,folder,notes);OpenCreatedProject(project);return project;
    }
    private ProjectMemory SaveChatProject(string name,string folder,string notes)
    {
        name=name.Trim();folder=folder.Trim();
        if(VisibleProjects.Any(p=>string.Equals(p.Name.Trim(),name,StringComparison.OrdinalIgnoreCase)))throw new IOException("A project with that name already exists. Choose another name or open the existing project.");
        if(folder.Length>0&&!Directory.Exists(folder))throw new IOException("Choose an existing folder, or leave the folder empty.");
        var project=new ProjectMemory{Name=name,Purpose=notes,Roots=folder.Length==0?[]:[Path.GetFullPath(folder)]};
        SaveProject(project,select:false);
        return project;
    }
    private void OpenCreatedProject(ProjectMemory project)
    {
        try{SelectProjectChats(project.Id);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){throw new IOException("Project saved, but its chat could not open: "+e.Message,e);}
        status.Text="Project created: "+project.Name;
    }
}

internal sealed class NewProjectWindow:Window
{
    private readonly TextBox name=new(){MaxLength=120},folder=new(),notes=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=100,MaxLength=16000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap};
    internal string ProjectName=>name.Text;
    internal string Folder=>folder.Text;
    internal string Notes=>notes.Text;
    internal NewProjectWindow(IEnumerable<string>? existingNames=null,string? initialFolder=null,Action<string,string,string>? save=null)
    {
        Title="New project";Width=560;Height=600;MinWidth=440;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        if(!string.IsNullOrWhiteSpace(initialFolder)&&Directory.Exists(initialFolder)){folder.Text=initialFolder;var leaf=Path.GetFileName(Path.TrimEndingDirectorySeparator(initialFolder));if(!string.IsNullOrWhiteSpace(leaf)){var used=(existingNames??[]).Select(n=>n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);var candidate=leaf;for(var suffix=2;used.Contains(candidate);suffix++)candidate=leaf+" "+suffix;name.Text=candidate;}}
        var shell=new DockPanel{Margin=new Thickness(24)};var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text="Create a project",FontSize=22,Margin=new Thickness(0,0,0,14)});
        void Field(string label,TextBox input){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,5)});System.Windows.Automation.AutomationProperties.SetName(input,label);panel.Children.Add(input);}
        Field("Project name",name);panel.Children.Add(new TextBlock{Text="Folder (optional)",Margin=new Thickness(0,8,0,5)});System.Windows.Automation.AutomationProperties.SetName(folder,"Folder (optional)");panel.Children.Add(FolderPicker.Row(this,folder,"Choose project folder"));
        Field("Instructions (optional)",notes);
        panel.Children.Add(new TextBlock{Text="Chats share these instructions. The AI can read and write screened files in the connected folder. Overwrites keep backups; deletes and commands require review.",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,10,0,8)});
        error.TextWrapping=TextWrapping.Wrap;error.Margin=new Thickness(0,8,0,0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(error,"ProjectCreationError");
        error.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)){Source=error});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);shell.Children.Add(actions);DockPanel.SetDock(error,Dock.Bottom);shell.Children.Add(error);
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);
        var create=new Button{Content="Create project",IsDefault=true};actions.Children.Add(create);create.Click+=(_,_)=>
        {
            try
            {
                if(string.IsNullOrWhiteSpace(name.Text))throw new IOException("Enter a project name.");
                if((existingNames??[]).Any(n=>string.Equals(n.Trim(),name.Text.Trim(),StringComparison.OrdinalIgnoreCase)))throw new IOException("A project with this name already exists.");
                if(folder.Text.Trim().Length>0&&!Directory.Exists(folder.Text.Trim()))throw new IOException("Choose an existing folder, or leave it empty.");
                new ProjectMemory{Name=name.Text.Trim(),Roots=folder.Text.Trim().Length==0?[]:[folder.Text.Trim()],Purpose=notes.Text}.Validate();
                save?.Invoke(name.Text.Trim(),folder.Text.Trim(),notes.Text);DialogResult=true;
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException){error.Text="Could not create project: "+e.Message;}
        };
        shell.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});DarkTheme.Wrap(this,shell);Loaded+=(_,_)=>name.Focus();
    }
    internal void TestSubmit(string title,string path="",string instructions=""){name.Text=title;folder.Text=path;notes.Text=instructions;DescendantCreate().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    internal void TestChooseFolder(string path)
    {
        var prior=FolderPicker.TestSelection;
        try{FolderPicker.TestSelection=_=>path;FindButtons((DependencyObject)Content).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Choose project folder").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
        finally{FolderPicker.TestSelection=prior;}
    }
    internal string TestError=>error.Text;
    private Button DescendantCreate()=>FindButtons((DependencyObject)Content).Single(b=>Equals(b.Content,"Create project"));
    private static IEnumerable<Button> FindButtons(DependencyObject node){foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){if(child is Button button)yield return button;foreach(var nested in FindButtons(child))yield return nested;}}
}
