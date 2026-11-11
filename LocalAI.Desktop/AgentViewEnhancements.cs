using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal sealed partial class AgentView
{
    private sealed record Choice(Guid? Id,string Label){public override string ToString()=>Label;}
    private readonly ComboBox projectPicker=new(){MinWidth=220,MaxWidth=560};
    private readonly ComboBox destination=new(){MinWidth=230,MaxWidth=850,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly TextBlock projectPath=new(){TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12,Opacity=0.8};
    private readonly TextBlock activity=new(){TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,4,0,8)};
    private readonly CheckBox showHandled=new(){Content="Show handled",Margin=new Thickness(3,7,3,7)};
    private string projectsKey="",tasksKey="";
    private void InitializeEnhancements(StackPanel header,StackPanel footer,Panel controls,DockPanel taskPane,DockPanel approvalPane)
    {
        var introduction=header.Children.OfType<TextBlock>().FirstOrDefault(t=>t.Text.StartsWith("One conversation",StringComparison.Ordinal));if(introduction!=null)header.Children.Remove(introduction);
        var help=header.Children.OfType<TextBlock>().FirstOrDefault(t=>t.Text.StartsWith("Works while",StringComparison.Ordinal));if(help!=null){help.Text="Changes and commands require review. A reply does not certify project completion.";help.FontSize=12;help.Margin=new Thickness(0,2,0,8);}
        var projectBar=new DockPanel{Margin=new Thickness(0,4,0,2)};
        projectBar.Children.Add(new TextBlock{Text="Project",Margin=new Thickness(0,6,10,0)});projectBar.Children.Add(projectPicker);
        var manage=new Button{Content="Manage project",Margin=new Thickness(8,0,0,0)};manage.Click+=(_,_)=>host.OpenAgentWorkbench("Projects");projectBar.Children.Add(manage);var pause=new Button{Content="Pause work",Margin=new Thickness(8,0,8,0)};pause.Click+=(_,_)=>host.AgentPause();projectBar.Children.Add(pause);projectBar.Children.Add(projectPath);
        header.Children.Insert(1,projectBar);header.Children.Insert(2,activity);
        var targetBar=new DockPanel{Margin=new Thickness(0,6,0,6)};targetBar.Children.Add(new TextBlock{Text="Send to",Margin=new Thickness(0,6,10,0)});targetBar.Children.Add(destination);footer.Children.Insert(1,targetBar);
        System.Windows.Automation.AutomationProperties.SetName(projectPicker,"Active Agent project");System.Windows.Automation.AutomationProperties.SetName(destination,"Agent message destination");System.Windows.Automation.AutomationProperties.SetName(activity,"Agent work status");
        tasks.ItemTemplate=AgentPresentation.Template(value=>value is AgentTask task?AgentPresentation.TaskLabel(task):"");approvals.ItemTemplate=AgentPresentation.Template(value=>value is AgentApproval approval?AgentPresentation.ApprovalLabel(approval):"");
        tasks.HorizontalContentAlignment=HorizontalAlignment.Stretch;approvals.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var taskActions=new WrapPanel();DockPanel.SetDock(taskActions,Dock.Bottom);taskPane.Children.Insert(1,taskActions);
        Add(taskActions,"Follow up selected task",()=>{if(tasks.SelectedItem is not AgentTask task)throw new IOException("Select a task first.");host.SaveAgentDestination(task.Id,composer.Text);Refresh();composer.Focus();notice.Text="Your next prompt goes to “"+task.Title+"”. Press Send follow-up to continue the same task.";return Task.CompletedTask;});
        var reviewActions=new WrapPanel();DockPanel.SetDock(reviewActions,Dock.Bottom);approvalPane.Children.Insert(1,reviewActions);Add(reviewActions,"Review selected proposal",async()=>{if(approvals.SelectedItem is not AgentApproval approval)throw new IOException("Select a proposal to review.");if(approval.Status=="Pending review")await host.AgentReview(approval);else host.OpenAgentWorkbench("Changes");});reviewActions.Children.Add(showHandled);
        if(taskPane.Parent is Grid panes)panes.RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);
        approvalPane.Margin=new Thickness(0);
        showHandled.Checked+=(_,_)=>Refresh();showHandled.Unchecked+=(_,_)=>Refresh();
        projectPicker.SelectionChanged+=(_,_)=>{if(refreshing||projectPicker.SelectedItem is not Choice c)return;try{host.SelectAgentProject(c.Id);}catch(Exception e){notice.Text=e.Message;Refresh();}};
        destination.SelectionChanged+=(_,_)=>{if(refreshing||destination.SelectedItem is not Choice c)return;try{host.SaveAgentDestination(c.Id,composer.Text);Refresh();}catch(Exception e){notice.Text=e.Message;Refresh();}};
        approvals.SelectionChanged+=(_,_)=>{if(approvals.SelectedItem is AgentApproval a)notice.Text=AgentPresentation.ApprovalLabel(a);};
    }
    private void RefreshEnhancements()
    {
        var pkey=string.Join("|",state.Projects.Where(p=>p.DeletedAt==null).Select(p=>p.Id+":"+p.Name));
        if(projectPicker.ItemsSource==null||projectsKey!=pkey){projectPicker.ItemsSource=new[]{new Choice(null,"General workspace")}.Concat(state.Projects.Where(p=>p.DeletedAt==null).Select(p=>new Choice(p.Id,p.Name))).ToArray();projectsKey=pkey;}
        projectPicker.SelectedItem=projectPicker.Items.OfType<Choice>().FirstOrDefault(c=>c.Id==state.ActiveProjectId);
        var project=state.Projects.FirstOrDefault(p=>p.Id==state.ActiveProjectId);var roots=project?.Roots??state.Roots;projectPath.Text=roots.Count==0?"   No folders connected":"   "+string.Join(" · ",roots);projectPath.ToolTip=string.Join("\n",roots);
        var tkey=string.Join("|",state.Tasks.Select(t=>t.Id+":"+t.Title));
        if(destination.ItemsSource==null||tasksKey!=tkey){destination.ItemsSource=new[]{new Choice(null,"Ongoing Agent conversation")}.Concat(state.Tasks.Select(t=>new Choice(t.Id,"Follow up: "+t.Title))).ToArray();tasksKey=tkey;}
        destination.SelectedItem=destination.Items.OfType<Choice>().FirstOrDefault(c=>c.Id==state.DraftTaskId);
        var target=state.Tasks.FirstOrDefault(t=>t.Id==state.DraftTaskId);
        if(state.DraftTaskId.HasValue&&target==null){activity.Text="Saved task destination is unavailable. Choose a destination; your prompt is retained.";}
        else
        {
            var pending=state.Approvals.Count(a=>a.Status is "Pending review" or "Reviewing");
            activity.Text=host.AgentAwaitingReview?host.AgentWorkLabel:host.AgentIsWorking?(host.AgentStopRequested?"Stopping · waiting for the active reply to stop":host.AgentWorkLabel):state.Paused?"Paused · saved work retained"+(pending>0?$" · {pending} review{(pending==1?"":"s")} waiting":""):pending>0?$"Waiting for review · {pending} item{(pending==1?"":"s")}":state.Tasks.Any(t=>t.Status=="Queued")?"Task queued · waiting to start":"Ready for next prompt · no work is running";
            if(target!=null)activity.Text+="\nTask project: "+(state.Projects.FirstOrDefault(p=>p.Id==target.ProjectId)?.Name??"General workspace")+" · "+(target.Status=="Response ready"?"Reply received; completion unverified":target.Status);
        }
        activity.Foreground=new SolidColorBrush(host.AgentIsWorking?Color.FromRgb(164,186,255):state.Paused?Color.FromRgb(183,193,210):Color.FromRgb(221,196,139));
        projectPicker.IsEnabled=!host.AgentIsWorking;destination.IsEnabled=!sending;model.IsEnabled=!host.AgentIsWorking&&target==null;
        if(target!=null)model.SelectedItem=model.Items.OfType<ModelProfile>().FirstOrDefault(p=>p.Id==target.ModelId);
        send.Content=state.DraftTaskId.HasValue?"Send follow-up":"Send";
        send.IsEnabled=!sending&&(!host.AgentIsWorking||target!=null);followMode.IsEnabled=target!=null;
        if(target!=null&&target.FollowUps.Any(f=>f.Status=="Pending"))activity.Text+=" · "+target.FollowUps.Count(f=>f.Status=="Pending")+" follow-ups waiting";
        var selected=approvals.SelectedItem;approvals.ItemsSource=state.Approvals.Where(a=>showHandled.IsChecked==true||AgentPresentation.VisibleReview(a)).OrderBy(a=>a.Status=="Pending review"?0:1).ToArray();approvals.SelectedItem=selected;
        System.Windows.Automation.AutomationProperties.SetName(this,"Infinity Agent"+(host.AgentAwaitingReview?" • review needed":host.AgentIsWorking?" • working":state.Paused?" • paused":""));
    }
    internal string TestActivity=>activity.Text;
    internal string TestProject=>projectPicker.SelectedItem?.ToString()??"";
    internal string TestDestination=>destination.SelectedItem?.ToString()??"";
    // Two caption lines and row padding must remain visible in each list.
    internal bool TestPanesReadable=>tasks.ActualHeight>=48&&approvals.ActualHeight>=48;
    internal bool TestHasApproval(Guid id)=>approvals.Items.Cast<AgentApproval>().Any(a=>a.Id==id);
    internal void TestChooseDestination(Guid? id)=>destination.SelectedItem=destination.Items.OfType<Choice>().Single(c=>c.Id==id);
}
