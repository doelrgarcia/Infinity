using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class AgentWorkbenchView : UserControl
{
    private readonly MainWindow host;
    private readonly AgentState state;
    private readonly DockPanel content=new();
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,MaxHeight=70};
    private string current="";
    private bool working;
    private CancellationTokenSource? search;
    private readonly Dictionary<string,Button> navigation=[];
    internal AgentWorkbenchView(MainWindow host,AgentState state)
    {
        this.host=host;this.state=state;FontSize=12;Foreground=host.Foreground;Background=host.Background;var body=new DockPanel{Margin=new Thickness(12,0,0,0)};Content=body;
        var nav=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(nav,Dock.Top);body.Children.Add(nav);Button(nav,"Agent conversation",()=>{host.OpenAgent();return Task.CompletedTask;});foreach(var name in new[]{"Projects","Changes","Verification","Search","Inbox","Routing"}){navigation[name]=Button(nav,name,()=>{ShowView(name);return Task.CompletedTask;});System.Windows.Automation.AutomationProperties.SetName(navigation[name],"Open Agent "+name+" workspace");}DockPanel.SetDock(notice,Dock.Bottom);body.Children.Add(notice);body.Children.Add(content);

    }
    internal void Refresh(){if(working)return;if(current is "Inbox" or "Verification" or "Changes")ShowView(current);}
    internal void ShowView(string name)
    {
        current=name;content.Children.Clear();notice.Text="";
        switch(name){case "Projects":Projects();break;case "Changes":Changes();break;case "Verification":Verification();break;case "Search":Search();break;case "Inbox":Inbox();break;case "Routing":Routing();break;}
    }
    private Button Button(Panel parent,string text,Func<Task> action)
    {var button=new Button{Content=text,Margin=new Thickness(3),Padding=new Thickness(10,7,10,7)};parent.Children.Add(button);button.Click+=async(_,_)=>{if(working)return;working=true;try{await action();}catch(Exception e){notice.Text=e.Message;}finally{working=false;if(current is "Inbox" or "Changes" or "Verification"){var message=notice.Text;ShowView(current);notice.Text=message;}}};return button;}
    private StackPanel Header(string title,string description)
    {var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);content.Children.Add(header);header.Children.Add(new TextBlock{Text=title,FontSize=24});header.Children.Add(new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,12)});return header;}
    private TextBox Field(Panel form,string title,string value,bool multiline=true,bool folder=false)
    {form.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,9,0,4)});var box=new TextBox{Text=value,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?55:30,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};System.Windows.Automation.AutomationProperties.SetName(box,title);if(folder){var row=new DockPanel();var button=FolderPicker.Button(host,box,"Add project folder",append:true);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(box);form.Children.Add(row);}else form.Children.Add(box);return box;}
    private void Projects()
    {
        var header=Header("Project memory","Keep purpose, conventions, build/test commands and unfinished work with the project. Commands are saved as plans; they run only after review.");var picker=new ComboBox{ItemsSource=state.Projects.Where(p=>p.DeletedAt==null).ToArray(),MinWidth=240};header.Children.Add(picker);
        var form=new StackPanel();content.Children.Add(new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var selected=state.Projects.FirstOrDefault(p=>p.Id==state.ActiveProjectId);Guid editId=selected?.Id??Guid.NewGuid();
        var name=Field(form,"Project name",selected?.Name??"",false);var roots=Field(form,"Connected folders — one full path per line",string.Join('\n',selected?.Roots??state.Roots),true,true);var purpose=Field(form,"Purpose",selected?.Purpose??"");var conventions=Field(form,"Conventions and decisions",selected?.Conventions??"");var build=Field(form,"Build command (PowerShell)",selected?.BuildCommand??"");var test=Field(form,"Test command (PowerShell)",selected?.TestCommand??"");var unfinished=Field(form,"Unfinished work",selected?.UnfinishedWork??"");
        picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is not ProjectMemory p)return;editId=p.Id;name.Text=p.Name;roots.Text=string.Join('\n',p.Roots);purpose.Text=p.Purpose;conventions.Text=p.Conventions;build.Text=p.BuildCommand;test.Text=p.TestCommand;unfinished.Text=p.UnfinishedWork;};picker.SelectedItem=selected;
        var buttons=new WrapPanel();form.Children.Add(buttons);Button(buttons,"Save & use project",()=>{host.SaveProject(new(){Id=editId,Name=name.Text,Roots=roots.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList(),Purpose=purpose.Text,Conventions=conventions.Text,BuildCommand=build.Text,TestCommand=test.Text,UnfinishedWork=unfinished.Text});notice.Text="Project saved and selected. Existing tasks keep their original project.";picker.ItemsSource=null;picker.ItemsSource=state.Projects.Where(p=>p.DeletedAt==null).ToArray();return Task.CompletedTask;});Button(buttons,"New project",()=>{editId=Guid.NewGuid();picker.SelectedItem=null;name.Clear();purpose.Clear();conventions.Clear();build.Clear();test.Clear();unfinished.Clear();return Task.CompletedTask;});Button(buttons,"Use project for current coding chat",()=>{var id=picker.SelectedItem is ProjectMemory chosen?chosen.Id:editId;if(AgentView.Confirm(host,"Attach project to coding chat","Attach this saved project’s notes and folder scope to the current coding chat? Existing history and included source snapshots remain. Start a new chat first if this is a different project."))host.AttachProjectToCurrentChat(id);return Task.CompletedTask;});Button(buttons,"Use general workspace",()=>{host.SelectAgentProject(null);notice.Text="General workspace selected. Saved project memory retained.";return Task.CompletedTask;});
    }
    private void Changes()
    {
        Header("Changes","Review related edits together. Apply still requires each exact file review. Rollback is a new reviewed proposal and refuses files edited since apply.");
        var list=new ListBox{Height=170,ItemTemplate=AgentPresentation.Template(value=>value is AgentApproval approval?AgentPresentation.ApprovalLabel(approval):value?.ToString()??""),ItemsSource=state.Approvals.Where(a=>a.Kind=="files"&&AgentPresentation.VisibleReview(a)).Cast<object>().Concat(state.Changes.AsEnumerable().Reverse()).ToArray()};DockPanel.SetDock(list,Dock.Top);content.Children.Add(list);var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);content.Children.Add(actions);var details=FileActionReviewWindow.TextArea();details.TextWrapping=TextWrapping.NoWrap;content.Children.Add(details);
        list.SelectionChanged+=(_,_)=>{try{if(list.SelectedItem is TrackedChange c)details.Text=AgentWorkspaceFeatures.Diff(c.Plan)+"\n\n"+c.Status+"\n"+JsonSerializer.Serialize(c.Receipt);else if(list.SelectedItem is AgentApproval a){var roots=a.Roots.Count>0?a.Roots:state.Roots;details.Text=string.Join("\n\n",FileActionProtocol.Parse(a.Value).Select(action=>{try{return AgentWorkspaceFeatures.Diff(new FileWorkspace(roots).Preview(action));}catch(Exception e){return action.Operation+" "+action.Path+"\nProposed content:\n"+action.Content+"\nPreview unavailable until prior actions finish: "+e.Message;}}));}}catch(Exception e){notice.Text=e.Message;}};
        Button(actions,"Review & apply proposal",async()=>{if(list.SelectedItem is AgentApproval a)await host.AgentReview(a);else throw new IOException("Select a pending file proposal.");});Button(actions,"Queue rollback",async()=>{if(list.SelectedItem is TrackedChange c)await host.QueueRollback(c);else throw new IOException("Select an applied change.");});
    }
    private void Verification()
    {
        Header("Verified task completion","A verified badge requires reviewed successful build AND test runs for this task, matching the project's current source snapshot and configured commands. The command must actually test the behavior you need.");
        var jobs=new ListBox{ItemsSource=state.Tasks,ItemTemplate=AgentPresentation.Template(value=>value is AgentTask task?AgentPresentation.TaskLabel(task):""),Height=150};DockPanel.SetDock(jobs,Dock.Top);content.Children.Add(jobs);var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);content.Children.Add(actions);var details=FileActionReviewWindow.TextArea();content.Children.Add(details);
        jobs.SelectionChanged+=(_,_)=>{if(jobs.SelectedItem is AgentTask t)details.Text=t.Title+"\n"+t.Status+"\nVerified at: "+t.VerifiedAt+"\n\n"+string.Join("\n\n",t.Evidence.Select(e=>$"{e.Kind}: {e.Status} • exit {e.ExitCode}\n{e.Recorded}\n{e.Script}\n{e.Directory}\nSource: {e.SourceHash}\nLog: {e.LogPath}\n{e.Output}"));};
        Button(actions,"Review project build",async()=>{if(jobs.SelectedItem is AgentTask t)await host.ProposeTaskCheck(t,"build");});Button(actions,"Review project tests",async()=>{if(jobs.SelectedItem is AgentTask t)await host.ProposeTaskCheck(t,"test");});Button(actions,"Verify completion",async()=>{if(jobs.SelectedItem is AgentTask t){var result=await host.VerifyAgentTask(t);AgentView.ReviewText(host,"Task verification",result,false);}});
    }
    private void Search()
    {
        var header=Header("Search chats and files","Search saved chats and screened source files in the active project's connected folders. Up to 500 files and 100 results; nothing is sent to a model.");var query=new TextBox();header.Children.Add(query);var actions=new WrapPanel();header.Children.Add(actions);var results=new ListBox();content.Children.Add(results);
        var factory=new FrameworkElementFactory(typeof(TextBlock));factory.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Excerpt"));factory.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);results.ItemTemplate=new DataTemplate{VisualTree=factory};
        System.Windows.Automation.AutomationProperties.SetName(query,"Agent workspace search query");System.Windows.Automation.AutomationProperties.SetName(results,"Agent workspace search results");
        var searchButton=Button(actions,"Search",async()=>{search?.Cancel();search?.Dispose();search=new();results.ItemsSource=await host.SearchAgentWorkspace(query.Text,search.Token);notice.Text=$"{results.Items.Count} matches. Select one to open its chat or file.";});System.Windows.Automation.AutomationProperties.SetName(searchButton,"Search connected chats and files");var stop=new Button{Content="Stop search",Margin=new Thickness(3)};actions.Children.Add(stop);stop.Click+=(_,_)=>search?.Cancel();Button(actions,"Open result",async()=>{if(results.SelectedItem is WorkspaceSearchHit hit)await host.OpenSearchResult(hit);});results.SelectionChanged+=(_,_)=>{if(results.SelectedItem is WorkspaceSearchHit hit)notice.Text=hit.Kind+" • "+hit.Location;};
    }
    private void Inbox()
    {
        Header("Agent inbox",$"{state.Inbox.Count(n=>!n.Read)} unread updates • finished tasks, failures, activity changes and approvals. Reading an item never approves an action."+(state.InboxOverflowCount>0?$" Inbox capacity reached: {state.InboxOverflowCount} additional updates are retained in Tasks, Approvals or the Agent conversation.":""));var list=new ListBox{ItemsSource=state.Inbox.OrderByDescending(n=>n.Created).ToArray(),Height=220};DockPanel.SetDock(list,Dock.Top);content.Children.Add(list);var buttons=new WrapPanel();DockPanel.SetDock(buttons,Dock.Bottom);content.Children.Add(buttons);var text=FileActionReviewWindow.TextArea();text.TextWrapping=TextWrapping.Wrap;content.Children.Add(text);list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is AgentNotice item)text.Text=item.Created.ToLocalTime()+"\n"+item.Kind+"\n"+item.Text;};
        Button(buttons,"Mark read",()=>{if(list.SelectedItem is AgentNotice item){item.Read=true;host.AgentSaveDraft(state.Draft);}return Task.CompletedTask;});Button(buttons,"Review linked approval",async()=>{if(list.SelectedItem is AgentNotice item&&state.Approvals.SingleOrDefault(a=>a.Id==item.ApprovalId) is { } approval)await host.AgentReview(approval);else throw new IOException("This update has no linked approval.");});Button(buttons,"Task evidence",()=>{if(list.SelectedItem is AgentNotice item&&state.Tasks.SingleOrDefault(t=>t.Id==item.TaskId) is { } task)AgentView.ReviewText(host,task.Title,task.Detail+"\n\n"+JsonSerializer.Serialize(task.Evidence,new JsonSerializerOptions{WriteIndented=true}),false);return Task.CompletedTask;});Button(buttons,"Review stronger help",async()=>{if(list.SelectedItem is AgentNotice item&&state.Tasks.SingleOrDefault(t=>t.Id==item.TaskId) is { } task)host.AgentTaskStronger(task);else await host.AgentSendStronger();});
    }
    private void Routing()
    {
        var form=Header("Model routing","Use local AI first. On local errors, timeouts or oversized context, offer a reviewed stronger-model handoff. No cloud request is sent without exact-context consent.");var local=new CheckBox{Content="Local first",IsChecked=state.LocalFirst};var offer=new CheckBox{Content="Offer stronger-model help when local generation fails",IsChecked=state.OfferCloudHelp};form.Children.Add(local);form.Children.Add(offer);var providers=new ComboBox{ItemsSource=host.AgentModels.Where(m=>m.IsCloud).ToArray(),MinWidth=260,Margin=new Thickness(0,12,0,12)};providers.SelectedItem=host.AgentModels.FirstOrDefault(m=>m.Id==state.PreferredCloudId);form.Children.Add(providers);
        Button(form,"Save routing preferences",()=>{state.LocalFirst=local.IsChecked==true;state.OfferCloudHelp=offer.IsChecked==true;state.PreferredCloudId=(providers.SelectedItem as ModelProfile)?.Id??state.PreferredCloudId;host.AgentSaveDraft(state.Draft);notice.Text="Routing saved. Explicit stronger-model requests still require consent.";return Task.CompletedTask;});Button(form,"Ask stronger model with my Agent draft",async()=>await host.AgentSendStronger());
    }
    internal string TestView=>current;
    internal void TestNavigate(string view)=>navigation[view].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal bool IsWorking=>working;
    internal void CancelSearch()=>search?.Cancel();
}
public sealed partial class MainWindow
{
    internal IReadOnlyList<ModelProfile> AgentModels=>settings.Models;
    internal Task AgentSendStronger()
    {var text=Agent.Draft;if(string.IsNullOrWhiteSpace(text))text=Agent.Messages.LastOrDefault(m=>m.Role=="user"&&!m.Content.StartsWith("Untrusted tool result")&&!m.Content.StartsWith("Protocol error")&&!m.Content.StartsWith("Reviewed action receipt"))?.Content??"";if(string.IsNullOrWhiteSpace(text))throw new IOException("Write the request in the Agent composer first. It will be reviewed before sending.");return AgentSend(text,Agent.PreferredCloudId,true);}
    internal void AgentTaskStronger(AgentTask task)
    {if(task.Status=="Running"||Agent.Approvals.Any(a=>a.TaskId==task.Id&&a.Status is "Pending review" or "Reviewing"))throw new IOException("Finish the task's run and pending reviews first.");task.ModelId=Agent.PreferredCloudId;task.UseSelectedProvider=true;AgentResume(task);}
    internal async Task ProposeTaskCheck(AgentTask task,string kind)
    {
        var project=AgentProject(task)??throw new IOException("This task has no project. Create a task with a selected project first.");var script=kind=="build"?project.BuildCommand:project.TestCommand;if(string.IsNullOrWhiteSpace(script))throw new IOException("Configure the project's "+kind+" command first.");
        if(project.Roots.Count==0)throw new IOException("Connect a project folder before reviewing build or test commands.");
        var packet=QueueApproval("command",JsonSerializer.Serialize(new CommandRequest(script,project.Roots[0])),task);_ = packet;var approval=Agent.Approvals.Last();approval.Purpose=kind;SaveAgent();await AgentReview(approval);
    }
}
