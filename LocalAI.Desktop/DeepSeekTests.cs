using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task ExerciseDeepSeek(Dictionary<string,object> rows)
    {
        var profile=settings.Models.Single(p=>p.Engine=="deepseek");
        rows["deepSeekAppearsInMainPickerAndCloudRouting"]=model.Items.Cast<ModelProfile>().Contains(profile)&&profile.IsCloud&&AgentModels.Contains(profile);
        var test=new Settings();DeepSeekBridge.AddProvider(test);DeepSeekBridge.AddProvider(test);rows["deepSeekDiscoveryDoesNotDuplicateOrRewriteExistingProfiles"]=test.Models.Count==1&&test.Models[0].Engine=="deepseek";var collision=new Settings{Models=[new(){Id="cloud-deepseek",Engine="codex"}]};DeepSeekBridge.AddProvider(collision);rows["deepSeekDiscoveryDoesNotDuplicateOrRewriteExistingProfiles"]=(bool)rows["deepSeekDiscoveryDoesNotDuplicateOrRewriteExistingProfiles"]&&collision.Models.Select(m=>m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==2&&collision.Models[0].Engine=="codex";
        var transcript=new CliTranscript();var one=transcript.AppendEvent("{\"type\":\"text\",\"text\":\"Answer\"}","deepseek");transcript.AppendEvent("{\"type\":\"final\",\"finish\":\"max-tokens\"}","deepseek");rows["deepSeekTranscriptPreservesTextAndTokenLimit"]=one=="Answer"&&transcript.Text=="Answer"&&transcript.Finish=="max-tokens";
        bool Refuses(string json){try{new CliTranscript().AppendEvent(json,"deepseek");return false;}catch(IOException){return true;}}
        rows["deepSeekRejectsToolAndUnrecognizedEvents"]=Refuses("{\"type\":\"tool_call\"}")&&Refuses("{\"type\":\"unknown\"}");
        try{transcript.AppendEvent("{\"type\":\"text\",\"text\":\"late\"}","deepseek");rows["deepSeekRejectsPostCompletionReplay"]=false;}catch(IOException){rows["deepSeekRejectsPostCompletionReplay"]=true;}
        try{new CliTranscript().AppendEvent("{\"type\":\"error\",\"code\":\"ACCOUNT_SIGN_IN_REQUIRED\"}","deepseek");rows["deepSeekSignInFailureIsActionableWithoutApiKey"]=false;}catch(IOException e){rows["deepSeekSignInFailureIsActionableWithoutApiKey"]=e.Message.Contains("Harness")&&e.Message.Contains("No API key");}
        NewConversation();model.SelectedItem=profile;prompt.Text="Synthetic DeepSeek consent check";var before=conversation.Messages.Count;
        // Supply existing fixture executable solely to reach the real consent UI, then cancel.
        var executable=profile.Executable;profile.Executable=Environment.ProcessPath!;
        try{_=Dispatcher.BeginInvoke(()=>Application.Current.Windows.OfType<ProviderConsent>().Single().Close());await Send();rows["deepSeekCloudReviewCancelSendsNothingAndPreservesDraft"]=conversation.Messages.Count==before&&prompt.Text=="Synthetic DeepSeek consent check"&&status.Text.Contains("cancelled before sending");}
        finally{profile.Executable=executable;}
        chatProjectFilter=null;NewConversation();files.Clear();webSources.Clear();conversation.WorkspaceRoots=[];
        var packet=BuildMessages("Reply with exactly: DeepSeek connection verified.");
        File.WriteAllText(Path.ChangeExtension(smokeOutput!,".deepseek-review.json"),JsonSerializer.Serialize(packet.Select(m=>new{role=m.Role,content=FileAttachment.InputText(m)})));
        File.WriteAllText(Path.ChangeExtension(smokeOutput!,".deepseek-review.txt"),CliAssistant.FormatPrompt(packet));
        model.SelectedItem=settings.Models.First(p=>!p.IsCloud);
    }
}
