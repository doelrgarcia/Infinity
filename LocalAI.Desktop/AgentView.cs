using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed partial class AgentView : UserControl
{
    private readonly MainWindow host;
    private readonly AgentState state;
    private readonly TextBox transcript=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBox composer=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=80,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ComboBox followMode=new(){ItemsSource=new[]{"Queue","Steer"},SelectedIndex=0,Width=90,ToolTip="Queue after the reply, or steer at the next safe model step. Already executed actions are retained."};
    private readonly ComboBox model=new(){MinWidth=180};
    private readonly ListBox tasks=new(),approvals=new();
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,MaxHeight=62,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly Button send=new(){Content="Send"},microphone=new(){Content="● Speak"};
    private readonly CheckBox trustedWork=new(){Content="Let the AI work automatically",ToolTip="The Agent applies file edits, folder creation, copy/move and single-file deletes in connected folders, and runs non-destructive commands. Folder or mass deletes, destructive commands and browser form actions still require review.",Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Button workspaceEntry=new(){Content="Workspace & inbox",Margin=new Thickness(3),Padding=new Thickness(8)};
    private IDictationSession? voice;
    internal Func<IDictationSession>? VoiceSessionFactoryTest {get;set;}
    private bool sending,refreshing;
    internal AgentView(MainWindow host,AgentState state,IEnumerable<ModelProfile> models,string? selectedModel=null)
    {
        this.host=host;this.state=state;FontSize=12;Foreground=host.Foreground;Background=host.Background;
        trustedWork.IsChecked=state.TrustedWork;trustedWork.Checked+=(_,_)=>{if(refreshing)return;try{host.SetAgentTrustedWork(true);}catch(Exception e){notice.Text=e.Message;trustedWork.IsChecked=false;}};trustedWork.Unchecked+=(_,_)=>{if(refreshing)return;try{host.SetAgentTrustedWork(false);}catch(Exception e){notice.Text=e.Message;}};
        var body=new DockPanel{Margin=new Thickness(12,0,0,0)};Content=body;var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);header.Children.Add(new TextBlock{Text="Agent",FontSize=24});header.Children.Add(new TextBlock{Text="One conversation for your chats, coding tasks, browser and connected files.",Margin=new Thickness(0,6,0,6)});header.Children.Add(new TextBlock{Text="Works while Infinity is running and this PC is awake. Changes and commands require your review unless you enable trusted work. Task responses are not proof that a project is complete.",TextWrapping=TextWrapping.Wrap,Opacity=0.8,Margin=new Thickness(0,0,0,12)});
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);footer.Children.Add(notice);footer.Children.Add(actionOfferPanel);footer.Children.Add(attachmentTray);var trustedRow=new DockPanel{Margin=new Thickness(0,2,0,6)};trustedRow.Children.Add(trustedWork);trustedRow.Children.Add(new TextBlock{Text="Files and safe commands run automatically; folder/mass deletes and destructive commands still ask.",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Opacity=0.7,FontSize=11});footer.Children.Add(trustedRow);footer.Children.Add(composer);var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};footer.Children.Add(actions);model.ItemsSource=models;model.SelectedItem=models.FirstOrDefault(m=>m.Id==selectedModel)??models.FirstOrDefault(m=>!m.IsCloud)??models.First();foreach(var element in new UIElement[]{model,followMode,microphone,send})actions.Children.Add(element);
        Add(actions,"Attach files and images",()=>{PickAgentAttachments();return Task.CompletedTask;});
        Add(actions,"Stop reply",()=>{host.AgentStopReply();return Task.CompletedTask;});Add(actions,"Memory",()=>{var edited=ReviewText(host,"Agent memory — saved locally",state.Memory,true);if(edited!=null){state.Memory=edited;host.AgentSaveDraft(composer.Text);}return Task.CompletedTask;});
        actions.Children.Add(workspaceEntry);workspaceEntry.Click+=(_,_)=>host.OpenAgentWorkbench();
        Add(actions,"Connected folders",()=>{var edited=ReviewText(host,"Folders the Agent may list and read — one full path per line",string.Join("\n",host.AgentConnectedRoots),true,true);if(edited!=null){var roots=edited.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(roots.Count>16)throw new IOException("Connect at most 16 folders.");var workspace=new FileWorkspace(roots);foreach(var root in roots)workspace.Resolve(root);host.SetAgentFolders(roots);host.AgentSaveDraft(composer.Text);}return Task.CompletedTask;});
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.Children.Add(grid);grid.Children.Add(transcript);var side=new DockPanel{Margin=new Thickness(14,0,0,0)};Grid.SetColumn(side,1);grid.Children.Add(side);var controls=new WrapPanel();var moreActions=new Expander{Header="More task actions",Content=controls,Foreground=Foreground,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(moreActions,Dock.Bottom);side.Children.Add(moreActions);
        Add(controls,"Pause work & watches",()=>{host.AgentPause();return Task.CompletedTask;});Add(controls,"Resume selected task",()=>{host.AgentResume(tasks.SelectedItem as AgentTask);return Task.CompletedTask;});Add(controls,"Stop selected task",()=>{host.AgentCancelTask(tasks.SelectedItem as AgentTask);return Task.CompletedTask;});Add(controls,"Review approval",async()=>{if(approvals.SelectedItem is AgentApproval a)await host.AgentReview(a);});Add(controls,"Decline approval",async()=>{if(approvals.SelectedItem is AgentApproval a)await host.AgentReview(a,true);});
        Add(controls,"Resume watches",()=>{host.AgentResumeWatches();return Task.CompletedTask;});Add(controls,"Task details",()=>{if(tasks.SelectedItem is AgentTask t)ReviewText(host,t.Title,t.Instructions+"\n\n"+t.Detail,false);return Task.CompletedTask;});
        var sections=new Grid();sections.RowDefinitions.Add(new());sections.RowDefinitions.Add(new());side.Children.Add(sections);var taskPane=new DockPanel();sections.Children.Add(taskPane);var taskLabel=new TextBlock{Text="Tasks",FontSize=17,Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(taskLabel,Dock.Top);taskPane.Children.Add(taskLabel);taskPane.Children.Add(tasks);var approvalPane=new DockPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(approvalPane,1);sections.Children.Add(approvalPane);var approvalLabel=new TextBlock{Text="Approvals",FontSize=17,Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(approvalLabel,Dock.Top);approvalPane.Children.Add(approvalLabel);approvalPane.Children.Add(approvals);tasks.SelectionChanged+=(_,_)=>{if(tasks.SelectedItem is AgentTask t)notice.Text=t.Detail;};approvals.SelectionChanged+=(_,_)=>{if(approvals.SelectedItem is AgentApproval a)notice.Text=a.Id+" • "+a.Kind+" • "+a.Status;};
        InitializeEnhancements(header,footer,controls,taskPane,approvalPane);
        composer.Text=state.Draft;composer.TextChanged+=(_,_)=>{if(!refreshing){state.Draft=composer.Text;}};
        send.Click+=async(_,_)=>await Send();microphone.Click+=(_,_)=>ToggleVoice();System.Windows.Automation.AutomationProperties.SetName(composer,"Message Infinity Agent");System.Windows.Automation.AutomationProperties.SetName(transcript,"Agent conversation");System.Windows.Automation.AutomationProperties.SetName(microphone,"Dictate Agent prompt");
        PreviewKeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&voice!=null){voice.Dispose();voice=null;microphone.Content="● Speak";notice.Text="Dictation cancelled.";e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Enter&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;await Send();}};
        InitializeAttachmentDrop();Loaded+=(_,_)=>transcript.ScrollToEnd();Refresh();
    }
    internal void SaveDraftAndStopVoice(){StopVoice();host.AgentSaveDraft(composer.Text);}
    internal void StopVoice(){voice?.Dispose();voice=null;microphone.Content="● Speak";}
    internal void FocusComposer()=>composer.Focus();
    private void Add(Panel panel,string label,Func<Task> action){var button=new Button{Content=label,Margin=new Thickness(3),Padding=new Thickness(8)};panel.Children.Add(button);button.Click+=async(_,_)=>{try{await action();var message=notice.Text;Refresh();notice.Text=message;}catch(Exception e){notice.Text=e.Message;}};}
    internal void Refresh()
    {
        refreshing=true;
        try
        {
            workspaceEntry.Content="Workspace & inbox"+(state.Inbox.Count(n=>!n.Read)>0?$" ({state.Inbox.Count(n=>!n.Read)})":"");trustedWork.IsChecked=state.TrustedWork;
            var text=string.Join("\n\n",(state.Tasks.FirstOrDefault(t=>t.Id==state.DraftTaskId)?.Messages??state.Messages).Select(m=>
            {
                if(m.Role=="user")return m.Content.StartsWith("Untrusted tool result",StringComparison.Ordinal)||m.Content.StartsWith("Protocol error",StringComparison.Ordinal)?null:"You: "+m.Content+(m.Files?.Count>0?"\nAttached documents: "+string.Join(", ",m.Files.Select(f=>f.Name)):"")+(m.Images?.Count>0?"\nAttached images: "+string.Join(", ",m.Images.Select(i=>i.Name)):"");
                try{var action=AgentProtocol.Parse(m.Content);return action.Action=="reply"?"Agent: "+action.Value:"Agent used "+action.Action;}catch{return "Agent: "+m.Content;}
            }).Where(m=>m!=null));if(transcript.Text!=text){transcript.Text=text;transcript.ScrollToEnd();}
            var selected=tasks.SelectedItem;tasks.ItemsSource=null;tasks.ItemsSource=state.Tasks;tasks.SelectedItem=selected;var approval=approvals.SelectedItem;approvals.ItemsSource=null;approvals.ItemsSource=state.Approvals;approvals.SelectedItem=approval;
            if(sending&&composer.Text.Length>0&&state.Draft.Length==0)composer.Clear();
            RefreshEnhancements();RefreshActionOffer();RefreshAttachments();
        }
        finally{refreshing=false;}
    }
    private async Task Send()
    {
        if(sending||voice!=null)return;if(string.IsNullOrWhiteSpace(composer.Text)){if(AttachmentDraft.Files.Count+AttachmentDraft.Images.Count==0)return;composer.Text="Please process the attached files and images.";}var profile=model.SelectedItem as ModelProfile;if(state.DraftTaskId==null&&profile==null){notice.Text="Choose a model first. Your prompt is retained.";return;}sending=true;send.IsEnabled=false;notice.Text="Thinking… You can stop the reply or manage tasks.";
        try{if(state.DraftTaskId is {} id){var task=state.Tasks.SingleOrDefault(t=>t.Id==id)??throw new IOException("Task not found. Your prompt is retained.");host.AgentFollowUp(task,composer.Text,(string)followMode.SelectedItem,AttachmentDraft.Images.ToArray(),AttachmentDraft.Files.ToArray());AttachmentDraft.Files.Clear();AttachmentDraft.Images.Clear();composer.Clear();notice.Text="Follow-up saved in the same task; it waits at pending reviews. Steer applies at the next safe model step.";}else{var draft=AttachmentDraft;var text=composer.Text;var count=state.Messages.Count;await host.AgentSend(text,profile!.Id,images:draft.Images.ToArray(),files:draft.Files.ToArray());if(state.Messages.Skip(count).Any(m=>m.Role=="user"&&m.Content==text)){draft.Files.Clear();draft.Images.Clear();}notice.Text="Reply received. Review pending actions; completion is not yet verified.";}}catch(Exception e){notice.Text=e.Message;}finally{sending=false;send.IsEnabled=true;Refresh();}
    }
    private void ToggleVoice()
    {
        try
        {
            if(voice!=null){microphone.Content="Transcribing…";voice.Stop();return;}
            if(VoiceSessionFactoryTest==null&&!WhisperVoiceAssets.Installed)throw new IOException("Set up local voice with the microphone in the main chat first, then return here.");
            voice=VoiceSessionFactoryTest?.Invoke()??new WhisperDictation(Dispatcher);voice.Recognized+=text=>{composer.AppendText((composer.Text.Length>0?" ":"")+text);composer.CaretIndex=composer.Text.Length;};voice.Progress+=text=>notice.Text=text;voice.Completed+=error=>{voice?.Dispose();voice=null;microphone.Content="● Speak";notice.Text=error??"Dictation ready. Review it and press Send.";};microphone.Content="■ Stop recording";notice.Text="Listening locally. Press again to transcribe.";voice.Start();
        }
        catch(Exception e){voice?.Dispose();voice=null;microphone.Content="● Speak";notice.Text=e.Message;}
    }
    internal static string? ReviewText(Window owner,string title,string value,bool editable,bool chooseFolder=false)
    {
        var dialog=new Window{Owner=owner,Title=title,Width=Math.Min(800,SystemParameters.WorkArea.Width-24),Height=Math.Min(560,SystemParameters.WorkArea.Height-24),WindowStartupLocation=WindowStartupLocation.CenterOwner};DarkTheme.Apply(dialog);var body=new DockPanel{Margin=new Thickness(18)};DarkTheme.Wrap(dialog,body);var accept=new Button{Content=editable?"Save reviewed text":"Close",Padding=new Thickness(12)};DockPanel.SetDock(accept,Dock.Bottom);body.Children.Add(accept);var text=new TextBox{Text=value,IsReadOnly=!editable,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var textRow=new DockPanel();if(chooseFolder){var add=FolderPicker.Button(dialog,text,"Add folder",append:true);DockPanel.SetDock(add,Dock.Right);textRow.Children.Add(add);}textRow.Children.Add(text);body.Children.Add(textRow);accept.Click+=(_,_)=>{if(editable&&text.Text.Length>16000){DarkTheme.Error("Keep this under 16,000 characters.");return;}dialog.DialogResult=true;};return dialog.ShowDialog()==true?text.Text:null;
    }
    internal string TestTranscript=>transcript.Text;
    internal bool TestTranscriptAtEnd=>transcript.ExtentHeight>transcript.ViewportHeight&&Math.Abs(transcript.VerticalOffset-(transcript.ExtentHeight-transcript.ViewportHeight))<2;
    internal string TestComposer=>composer.Text;
    internal static bool Confirm(Window owner,string title,string detail)
    {
        var dialog=new Window{Owner=owner,Title=title,Width=Math.Min(850,SystemParameters.WorkArea.Width-24),Height=Math.Min(650,SystemParameters.WorkArea.Height-24),WindowStartupLocation=WindowStartupLocation.CenterOwner};DarkTheme.Apply(dialog);var body=new DockPanel{Margin=new Thickness(18)};DarkTheme.Wrap(dialog,body);var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);var approve=new Button{Content="Approve this exact action"};var cancel=new Button{Content="Cancel",Margin=new Thickness(8,0,0,0)};actions.Children.Add(approve);actions.Children.Add(cancel);body.Children.Add(new TextBox{Text=detail,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});approve.Click+=(_,_)=>dialog.DialogResult=true;cancel.Click+=(_,_)=>dialog.DialogResult=false;return dialog.ShowDialog()==true;
    }
    internal void TestDraft(string text)=>composer.Text=text;
    internal async Task TestSend(string text,string profile){model.SelectedItem=model.Items.OfType<ModelProfile>().Single(p=>p.Id==profile);composer.Text=text;await Send();}
}
