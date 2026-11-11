using System.Globalization;
using LocalAI.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalAI.Desktop;
internal interface IDictationSession : IDisposable
{
    event Action<string>? Recognized;
    event Action<string?>? Completed;
    event Action<string>? Progress;
    event Action<double>? Level;
    void Start();
    void Stop();
}
public sealed partial class MainWindow
{
    private readonly Button voiceMic=new(){Width=36,Height=32,Margin=new Thickness(0,0,6,0),Padding=new Thickness(7)};
    private readonly Button voiceDiscard=new(){Content="×",Width=30,Height=32,Margin=new Thickness(0,0,6,0),Padding=new Thickness(3),Visibility=Visibility.Collapsed,ToolTip="Cancel dictation (Esc)"};
    private readonly TextBlock voiceHint=new(){Text="● Listening… click the stop button when finished.",Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8),Foreground=Brush("#F2A4AF"),FontSize=12,TextWrapping=TextWrapping.Wrap};
    private IDictationSession? dictation;
    private Func<IDictationSession>? voiceSessionFactory;
    private Guid voiceOwner;
    private string voiceOriginal="",voiceText="";
    private int voiceAt,voiceSelection;
    private bool voiceStopping,voiceDownloadOffered;
    private readonly ComboBox microphoneInput=new(){MinWidth=180,MaxWidth=280,Margin=new Thickness(6,0,12,0)};
    private sealed record MicrophoneChoice(int Id,string Label);
    private readonly ProgressBar voiceLevel=new(){Minimum=0,Maximum=1,Width=70,Height=4,Margin=new Thickness(0,0,8,0),Visibility=Visibility.Collapsed};
    private void BuildVoiceControls(StackPanel actions,DockPanel composer)
    {
        var choices=new List<MicrophoneChoice>{new(-1,"Windows default microphone")};try{for(var i=0;i<NAudio.Wave.WaveInEvent.DeviceCount;i++)choices.Add(new(i,NAudio.Wave.WaveInEvent.GetCapabilities(i).ProductName));}catch{ }
        microphoneInput.ItemsSource=choices;var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));microphoneInput.ItemTemplate=new DataTemplate{VisualTree=label};microphoneInput.SelectedIndex=0;System.Windows.Automation.AutomationProperties.SetName(microphoneInput,"Dictation microphone");

        UpdateVoiceControls();voiceMic.Click+=(_,_)=>ToggleDictation();voiceDiscard.Click+=(_,_)=>CancelDictation();
        idleControls.Add(voiceMic);actions.Children.Add(voiceDiscard);actions.Children.Add(voiceMic);actions.Children.Add(voiceLevel);DockPanel.SetDock(voiceHint,Dock.Top);composer.Children.Add(voiceHint);
        System.Windows.Automation.AutomationProperties.SetName(voiceDiscard,"Cancel dictation");
        Closed+=(_,_)=>CancelDictation();
    }
    private void UpdateVoiceControls()
    {
        var recording=dictation!=null;voiceMic.Content=new Image{Source=UiIcons.Source(recording?"stop":"microphone"),Width=18,Height=18};
        voiceMic.ToolTip=recording?"Stop dictation (Ctrl+M)":"Dictate prompt (Ctrl+M) • audio stays local";
        System.Windows.Automation.AutomationProperties.SetName(voiceMic,recording?"Stop dictation":"Dictate prompt");
        voiceMic.IsEnabled=!busy&&!voiceStopping;voiceDiscard.Visibility=recording?Visibility.Visible:Visibility.Collapsed;voiceHint.Visibility=recording?Visibility.Visible:Visibility.Collapsed;
        voiceLevel.Visibility=recording?Visibility.Visible:Visibility.Collapsed;
        microphoneInput.IsEnabled=!recording&&!busy;
        prompt.IsReadOnly=recording;send.IsEnabled=!busy&&!recording;
    }
    private void ToggleDictation()
    {
        if(busy||voiceStopping)return;
        if(dictation!=null){voiceStopping=true;voiceHint.Text="Transcribing locally…";UpdateVoiceControls();dictation.Stop();return;}
        if(voiceSessionFactory==null&&!WhisperVoiceAssets.Installed&&!voiceDownloadOffered){voiceDownloadOffered=true;voiceHint.Text="Local voice needs a one-time 190 MB download. Click the microphone again to install, or × to cancel.";voiceHint.Visibility=Visibility.Visible;voiceDiscard.Visibility=Visibility.Visible;voiceMic.ToolTip="Download local Whisper voice model (190 MB)";return;}
        voiceDownloadOffered=false;voiceLevel.Value=0;
        voiceOriginal=prompt.Text;voiceAt=prompt.SelectionStart;voiceSelection=prompt.SelectionLength;voiceText="";voiceOwner=conversation.Id;
        var session=voiceSessionFactory?.Invoke()??new WhisperDictation(Dispatcher,(microphoneInput.SelectedItem as MicrophoneChoice)?.Id??-1);dictation=session;
        session.Progress+=text=>{if(ReferenceEquals(dictation,session))voiceHint.Text=text;};
        session.Level+=level=>{if(ReferenceEquals(dictation,session))voiceLevel.Value=level;};
        session.Recognized+=text=>{if(ReferenceEquals(dictation,session)&&voiceOwner==conversation.Id)AppendDictation(text);};
        session.Completed+=error=>{if(ReferenceEquals(dictation,session))FinishDictation(error);};
        voiceHint.Text="● Listening… click the stop button when finished.";UpdateVoiceControls();
        try{session.Start();}catch(Exception e){FinishDictation("Could not start the microphone: "+e.Message+" Check Windows microphone access and your default recording device.");}
    }
    private void AppendDictation(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        var addition=(voiceText.Length==0?"":" ")+text.Trim();voiceText+=addition[..Math.Min(addition.Length,Math.Max(0,16000-voiceText.Length))];
        var insertion=voiceText;if(voiceAt>0&&!char.IsWhiteSpace(voiceOriginal[voiceAt-1]))insertion=" "+insertion;
        var after=voiceAt+voiceSelection;if(after<voiceOriginal.Length&&!char.IsWhiteSpace(voiceOriginal[after]))insertion+=" ";
        prompt.Text=voiceOriginal[..voiceAt]+insertion+voiceOriginal[after..];prompt.CaretIndex=voiceAt+insertion.Length;prompt.ScrollToEnd();
    }
    private void FinishDictation(string? error)
    {
        var prior=dictation;dictation=null;voiceStopping=false;prior?.Dispose();UpdateVoiceControls();
        status.Text=error??(voiceText.Length==0?"No speech heard. Click the microphone to try again.":"Ready • edit your prompt or press Send.");prompt.Focus();
    }
    private void CancelDictation()
    {
        if(dictation==null){if(voiceDownloadOffered){voiceDownloadOffered=false;UpdateVoiceControls();}return;}
        var prior=dictation;dictation=null;voiceStopping=false;prior.Dispose();
        composerDrafts[voiceOwner]=voiceOriginal;
        var previous=syncingUi;syncingUi=true;try{prompt.Text=voiceOriginal;prompt.Select(voiceAt,voiceSelection);}finally{syncingUi=previous;}
        UpdateVoiceControls();status.Text="Dictation cancelled • original draft kept.";
    }
}
