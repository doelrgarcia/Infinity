using System.IO;
using LocalAI.Core;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LocalAI.Desktop;
internal sealed class TestDictation : IDictationSession
{
    public event Action<string>? Recognized;
    public event Action<string?>? Completed;
    public event Action<string>? Progress {add{} remove{}}
    public event Action<double>? Level {add{} remove{}}
    internal bool Started,Disposed;
    internal bool FailStart;
    internal bool CompleteStart;
    public void Start(){Started=true;if(FailStart)throw new InvalidOperationException("Synthetic microphone unavailable");if(CompleteStart)Completed?.Invoke("Synthetic immediate start failure");}
    public void Stop()=>Completed?.Invoke(null);
    public void Dispose()=>Disposed=true;
    internal void Say(string text)=>Recognized?.Invoke(text);
}
public sealed partial class MainWindow
{
    private void ExerciseInlineVoice(Dictionary<string,object> rows)
    {
        var windows=Application.Current.Windows.Count;prompt.Text="Existing draft.";prompt.Select(prompt.Text.Length,0);var messages=conversation.Messages.Count;
        var fake=new TestDictation();voiceSessionFactory=()=>fake;
        rows["microphoneOffUntilClicked"]=dictation==null;
        voiceMic.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        rows["oneClickStartsWithoutDialog"]=fake.Started&&dictation!=null&&Application.Current.Windows.Count==windows&&voiceHint.IsVisible;
        rows["recordingShowsStopAndPreventsSend"]=!send.IsEnabled&&prompt.IsReadOnly&&System.Windows.Automation.AutomationProperties.GetName(voiceMic)=="Stop dictation";
        fake.Say("Create an Acme website.");fake.Say("Keep it dark.");
        rows["speechAppearsDirectlyInPrompt"]=prompt.Text=="Existing draft. Create an Acme website. Keep it dark."&&conversation.Messages.Count==messages;
        voiceMic.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        rows["secondClickStopsAndEnablesEditing"]=dictation==null&&fake.Disposed&&!prompt.IsReadOnly&&send.IsEnabled&&prompt.Text.Contains("Acme");
        prompt.Text="Keep my draft.";prompt.Select(prompt.Text.Length,0);fake=new();voiceSessionFactory=()=>fake;ToggleDictation();fake.Say("Discard this.");voiceDiscard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        rows["cancelRestoresOriginalDraft"]=prompt.Text=="Keep my draft."&&dictation==null&&fake.Disposed;
        fake.Say("Late words must not appear");rows["lateSpeechAfterCancelIgnored"]=prompt.Text=="Keep my draft.";
        prompt.Text="Replace this word.";prompt.Select(8,4);fake=new();voiceSessionFactory=()=>fake;ToggleDictation();fake.Say("that");ToggleDictation();rows["dictationReplacesSelection"]=prompt.Text=="Replace that word.";
        prompt.Clear();fake=new();voiceSessionFactory=()=>fake;ToggleDictation();fake.Say(new string('x',18000));rows["dictationLengthBounded"]=prompt.Text.Length==16000;CancelDictation();
        prompt.Text="Survives microphone failure";fake=new(){FailStart=true};voiceSessionFactory=()=>fake;ToggleDictation();rows["microphoneFailureKeepsDraftAndAllowsRetry"]=dictation==null&&prompt.Text=="Survives microphone failure"&&voiceMic.IsEnabled&&status.Text.Contains("Could not start");
        fake=new();voiceSessionFactory=()=>fake;ToggleDictation();fake.Say("Old chat speech");var originalChat=conversation;NewConversation();rows["chatChangeCancelsWithoutLeakingText"]=fake.Disposed&&dictation==null&&prompt.Text==""&&composerDrafts[originalChat.Id]=="Survives microphone failure";
        voiceSessionFactory=null;ShowChat();UpdateLayout();
    }
}
internal static class VoicePromptTests
{
    internal static async Task Run(string output,bool hardware=false)
    {
        var rows=new Dictionary<string,object>();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);MainWindow? window=null;
        try
        {
            window=new MainWindow(["--ui-session",output]);window.Show();window.RunInlineVoiceTests(rows);QaCapture.Save(window,Path.ChangeExtension(output,".png"));rows["inlineComposerDarkRender"]=true;
            var pcmBytes=new byte[]{0,0,255,127,0,128};var converted=WhisperDictation.PcmSamples(pcmBytes);rows["pcmConversionClearsCaptureCopy"]=converted[0]==0&&converted[1]>0.99f&&converted[2]==-1&&pcmBytes.All(b=>b==0);
            rows["silenceDoesNotInventText"]=await WhisperDictation.TranscribeSamples(new float[16000],"missing-model-must-not-open",CancellationToken.None)=="";
            rows["shortRecordingDoesNotInventText"]=await WhisperDictation.TranscribeSamples(Enumerable.Repeat(0.1f,1000).ToArray(),"missing-model-must-not-open",CancellationToken.None)=="";
            if(hardware)
            {
                var model=await WhisperVoiceAssets.Ensure(null,CancellationToken.None);rows["localWhisperModelVerified"]=File.Exists(model);
                var wave=Path.ChangeExtension(output,".wav");using(var synth=new SpeechSynthesizer()){synth.SetOutputToWaveFile(wave);synth.Speak("Create a new website. Keep the background dark.");}
                using(var reader=new NAudio.Wave.WaveFileReader(wave))
                using(var resampler=new NAudio.Wave.MediaFoundationResampler(reader,new NAudio.Wave.WaveFormat(16000,16,1)))
                {using var pcm=new MemoryStream();var buffer=new byte[32000];int length;while((length=resampler.Read(buffer,0,buffer.Length))>0)pcm.Write(buffer,0,length);var samples=WhisperDictation.PcmSamples(pcm.ToArray());var text=await WhisperDictation.TranscribeSamples(samples,model,CancellationToken.None);File.WriteAllText(Path.ChangeExtension(output,".transcript.txt"),text);rows["realOfflineDictationFromAudio"]=text.Contains("website",StringComparison.OrdinalIgnoreCase)&&text.Contains("dark",StringComparison.OrdinalIgnoreCase);}
                using var mic=new WhisperDictation(window.Dispatcher);var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);mic.Completed+=error=>{if(error!=null)ended.TrySetException(new IOException(error));else ended.TrySetResult();};mic.Start();for(var wait=0;wait<100&&!mic.Listening&&!ended.Task.IsCompleted;wait++)await Task.Delay(100);rows["defaultMicrophoneStarts"]=mic.Listening;await Task.Delay(200);mic.Stop();await ended.Task.WaitAsync(TimeSpan.FromSeconds(90));rows["microphoneStops"]=!mic.Listening;
            }
            rows["closingAppReleasesMicrophone"]=window.TestCloseVoice();
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==(hardware?20:16)&&rows.Values.All(v=>v is true)?0:1);
    }
}
public sealed partial class MainWindow
{
    internal void RunInlineVoiceTests(Dictionary<string,object> rows)=>ExerciseInlineVoice(rows);
    internal bool TestCloseVoice(){var fake=new TestDictation();voiceSessionFactory=()=>fake;ToggleDictation();Close();return fake.Started&&fake.Disposed&&dictation==null;}
}
