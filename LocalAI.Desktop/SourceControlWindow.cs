using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal sealed class SourceControlWindow : Window
{
    private readonly string[] roots;
    private readonly Action<ProcessStartInfo> launch;
    private readonly Action<string> copy;
    private readonly TabControl tabs=new();
    private TextBox? teamsRecipients,teamsDraft;
    private CheckBox? teamsConsent;
    private TextBlock? teamsNote;
    private readonly ComboBox provider=new(){ItemsSource=Enum.GetValues<SourceProvider>(),SelectedIndex=0};
    private readonly TextBox folder=new(),tool=new(),file=new(),message=new();
    private readonly TextBox output=FileActionReviewWindow.TextArea();
    private readonly CheckBox trust=new(){Content="I trust this repository and the selected installed CLI (filters and credential helpers may run)."};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly List<Button> buttons=[];
    private readonly ComboBox operation=new();
    private readonly Button apply=new(){Content="Confirm reviewed repository action",IsEnabled=false};
    private SourcePlan? plan;
    private bool busy;
    private SourceProvider Provider=>(SourceProvider)provider.SelectedItem;
    private SourceControl Service=>new(roots,_=>tool.Text);
    public SourceControlWindow(IEnumerable<string> roots,string current,string selected,Action<ProcessStartInfo>? launch=null,Action<string>? copy=null)
    {
        this.launch=launch??(info=>Process.Start(info));this.copy=copy??Clipboard.SetText;
        this.roots=roots.ToArray();Title="Source control & team collaboration";Width=1100;Height=730;MinWidth=650;MinHeight=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        folder.Text=current;file.Text=selected;
        tabs.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
<Style TargetType="TabControl"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl"><DockPanel Background="#171B22"><TabPanel DockPanel.Dock="Top" IsItemsHost="True"/><ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent"/></DockPanel></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="TabItem"><Setter Property="Foreground" Value="#E6EBF5"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="B" Background="#20262F" BorderBrush="#303844" BorderThickness="1" Padding="14,10" CornerRadius="6,6,0,0"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="B" Property="Background" Value="#2D406B"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#83A3FF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""");
        var source=new DockPanel{Margin=new Thickness(14)};tabs.Items.Add(new TabItem{Header="Git / Azure DevOps / Subversion",Content=source});
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);source.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Use an existing working copy inside a connected folder. TFVC and SVN history may contact your server. Changes require review and confirmation. Git hooks and signing are disabled here; use your native client if repository policy requires them.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        top.Children.Add(provider);FolderRow(top,"Working-copy root",folder);Row(top,"Installed CLI (.exe)",tool);Row(top,"Selected file (required for add / commit / checkout)",file);Row(top,"Commit / check-in message",message);
        var reads=new WrapPanel();top.Children.Add(reads);foreach(var name in new[]{"status","diff","history","branches"})Button(reads,name,async()=>await Read(name));
        Button(reads,"Copy output",()=>{this.copy(output.Text);status.Text="Displayed output copied. Nothing was sent.";return Task.CompletedTask;});
        Button(reads,"Official client setup",()=>{this.launch(new ProcessStartInfo(SourceControl.SetupUrl(Provider)){UseShellExecute=true});return Task.CompletedTask;});
        Button(reads,"Azure DevOps web",()=>{this.launch(new ProcessStartInfo("https://dev.azure.com"){UseShellExecute=true});return Task.CompletedTask;});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);source.Children.Add(bottom);bottom.Children.Add(trust);bottom.Children.Add(operation);
        var review=new WrapPanel();bottom.Children.Add(review);Button(review,"Preview repository action",Preview);review.Children.Add(apply);buttons.Add(apply);bottom.Children.Add(status);source.Children.Add(output);
        apply.Click+=async(_,_)=>await Guard(Apply);provider.SelectionChanged+=(_,_)=>SelectProvider();
        foreach(var input in new[]{folder,tool,file,message})input.TextChanged+=(_,_)=>Invalidate();folder.TextChanged+=(_,_)=>trust.IsChecked=false;tool.TextChanged+=(_,_)=>trust.IsChecked=false;operation.SelectionChanged+=(_,_)=>Invalidate();trust.Unchecked+=(_,_)=>Invalidate();
        Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Waiting for the provider request to finish. Inspect status before retrying an interrupted request.";}};
        SelectProvider();tabs.Items.Add(new TabItem{Header="Microsoft Teams",Content=TeamsPanel()});DarkTheme.Wrap(this,tabs);
        foreach(var pair in new[]{(folder,"Repository root"),(tool,"Source control executable"),(file,"Source control selected file"),(message,"Commit message"),(output,"Source control output and review")})System.Windows.Automation.AutomationProperties.SetName(pair.Item1,pair.Item2);
        System.Windows.Automation.AutomationProperties.SetName(provider,"Source control provider");System.Windows.Automation.AutomationProperties.SetName(operation,"Repository operation");System.Windows.Automation.AutomationProperties.SetName(teamsRecipients!,"Teams draft recipients");
    }
    private static void Row(Panel panel,string label,UIElement input){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,5,0,2)});panel.Children.Add(input);}
    private void FolderRow(Panel panel,string label,TextBox input)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,5,0,2)});var row=new DockPanel();var button=FolderPicker.Button(this,input,"Choose working-copy root");DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(input);panel.Children.Add(row);buttons.Add(button);
    }
    private void SelectProvider()
    {
        tool.Text=SourceControl.FindTool(Provider)??"";
        operation.ItemsSource=Provider switch{SourceProvider.Git=>new[]{"stage","unstage","commit","fetch","push"},SourceProvider.Subversion=>new[]{"stage","commit"},_=>new[]{"stage","checkout","commit"}};operation.SelectedIndex=0;
        buttons.Single(b=>Equals(b.Content,"branches")).Visibility=Provider==SourceProvider.Git?Visibility.Visible:Visibility.Collapsed;
        status.Text=tool.Text.Length==0?"Client not found. Open Official client setup, install it, then select this provider again or enter its .exe path.":"Client found. Select an existing working-copy root and refresh status.";Invalidate();
    }
    private void Invalidate(){plan=null;apply.IsEnabled=false;}
    private void Button(Panel panel,string label,Func<Task> action)
    {var button=new Button{Content=label,Margin=new Thickness(0,5,6,5)};buttons.Add(button);panel.Children.Add(button);button.Click+=async(_,_)=>await Guard(action);}
    private SourceRequest Request(string action)=>new(Provider,folder.Text,action,file.Text,message.Text);
    private async Task Guard(Func<Task> action)
    {
        if(busy)return;busy=true;foreach(var b in buttons)b.IsEnabled=false;provider.IsEnabled=false;foreach(var box in new[]{folder,tool,file,message})box.IsEnabled=false;operation.IsEnabled=false;trust.IsEnabled=false;
        try{await action();}catch(Exception e){Invalidate();status.Text=e.Message;}
        finally{busy=false;foreach(var b in buttons)b.IsEnabled=true;apply.IsEnabled=plan!=null;provider.IsEnabled=true;foreach(var box in new[]{folder,tool,file,message})box.IsEnabled=true;operation.IsEnabled=true;trust.IsEnabled=true;}
    }
    private async Task Read(string action)
    {
        Invalidate();if(trust.IsChecked!=true)throw new IOException("Confirm repository and CLI trust before running installed tools.");status.Text="Reading "+action+"…";
        var result=await Service.Read(Request(action));output.Text=result.Output;status.Text=$"{Provider} {action} exited {result.ExitCode}. No content was added to a chat or Teams draft.";
    }
    private async Task Preview()
    {
        Invalidate();if(trust.IsChecked!=true)throw new IOException("Confirm repository and CLI trust before previewing.");status.Text="Preparing exact operation and current working-copy diff…";
        plan=await Service.Preview(Request((string)operation.SelectedItem));output.Text=plan.Review;status.Text="Review the exact folder, command and diff above. Confirm only when ready. Push/check-in publishes to your configured server.";
    }
    private async Task Apply()
    {
        var reviewed=plan??throw new IOException("Preview first.");plan=null;status.Text="Applying reviewed operation…";var result=await Service.Apply(reviewed,true,trust.IsChecked==true);output.Text=result.Output;status.Text=$"{reviewed.Request.Operation} exited {result.ExitCode}. Refresh status before continuing.";
    }
    private UIElement TeamsPanel()
    {
        var panel=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Prepare a team update here. Only the text you enter is shared. Opening a draft passes it to Microsoft Teams through a browser link; it can appear in browser history. Review it in Teams and send it yourself.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var recipients=new TextBox();teamsRecipients=recipients;Row(top,"Recipients (Teams email addresses, comma-separated)",recipients);
        top.Children.Add(new TextBlock{Text="Update draft (review before sharing)",Margin=new Thickness(0,10,0,6)});
        var draft=FileActionReviewWindow.TextArea();teamsDraft=draft;draft.IsReadOnly=false;System.Windows.Automation.AutomationProperties.SetName(draft,"Reviewed Teams draft");panel.Children.Add(draft);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Remove(draft);panel.Children.Add(footer);panel.Children.Add(draft);
        var consent=new CheckBox{Content="I reviewed these recipients and this draft and approve opening it in Microsoft Teams."};teamsConsent=consent;footer.Children.Add(consent);var note=new TextBlock{TextWrapping=TextWrapping.Wrap};teamsNote=note;var actions=new WrapPanel();footer.Children.Add(actions);footer.Children.Add(note);
        void TeamsButton(string label,Action action)=>Button(actions,label,()=>{try{action();}catch(Exception e){note.Text=e.Message;}return Task.CompletedTask;});
        TeamsButton("Copy draft",()=>{copy(draft.Text);note.Text="Draft copied. Nothing was sent.";});
        TeamsButton("Open reviewed Teams draft",()=>{if(consent.IsChecked!=true)throw new IOException("Review and approve the recipients and draft first.");var uri=TeamsIntegration.Draft(recipients.Text,draft.Text);launch(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});note.Text="Teams draft opened. Review it in Teams; sending remains your action.";});
        TeamsButton("Open Teams desktop / web",()=>launch(TeamsIntegration.Home()));
        recipients.TextChanged+=(_,_)=>consent.IsChecked=false;draft.TextChanged+=(_,_)=>consent.IsChecked=false;return panel;
    }
    internal async Task TestRead(){trust.IsChecked=true;await Guard(()=>Read("status"));}
    internal string TestOutput=>output.Text;
    internal async Task TestPreview(string selected){trust.IsChecked=true;file.Text=selected;operation.SelectedItem="stage";await Guard(Preview);}
    internal bool TestCanApply=>plan!=null&&apply.IsEnabled;
    internal Task TestApply()=>Guard(Apply);
    internal void TestTeamsTab()=>tabs.SelectedIndex=1;
    internal void TestTeamDraft(bool approved){teamsRecipients!.Text="qa@example.invalid";teamsDraft!.Text="Synthetic reviewed update";teamsConsent!.IsChecked=approved;TestClick("Open reviewed Teams draft");}
    internal void TestClick(string label)=>buttons.Single(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal bool TestTeamsNoteVisible=>teamsNote!.Text.Contains("approve",StringComparison.OrdinalIgnoreCase);
    internal bool TestDraftConsentResets(){teamsDraft!.Text+=" changed";return teamsConsent!.IsChecked==false;}
}
