using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class CopilotTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,bool>();Window? active=null;
        void Check(string name,bool passed){rows[name]=passed;if(!passed)throw new IOException(name);}
        try
        {
            var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"fixture");Directory.CreateDirectory(root);
            var exe=Path.Combine(root,OperatingSystem.IsWindows()?"copilot.exe":"copilot");File.WriteAllText(exe,"synthetic, never executed");
            Check("nativeDiscoveryIgnoresShellShims",CopilotBridge.FindExecutable([root])==exe&&CopilotBridge.FindExecutable(["relative"])==null);
            var settings=new Settings{Models=[new(){Id="cloud-copilot",Engine="codex"}]};CopilotBridge.AddProvider(settings,()=>exe);CopilotBridge.AddProvider(settings,()=>"changed.exe");var profile=settings.Models.Single(p=>p.Engine=="copilot");
            Check("discoveryPreservesProfilesAndUniqueIds",profile.Executable==exe&&profile.IsCloud&&settings.Models.Count==2&&settings.Models.Select(p=>p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==2);
            var missing=new Settings();CopilotBridge.AddProvider(missing,()=>null);Check("missingCliRemainsDiscoverableWithoutInstallation",missing.Models.Single().Executable=="");
            var config=Path.Combine(root,"settings.json");File.WriteAllText(config,JsonSerializer.Serialize(settings));Check("settingsRoundtripRecognizesCopilot",Settings.Load(config).Models.Single(p=>p.Engine=="copilot").Executable==exe);
            var start=CliAssistant.CreateStart(profile,root,"unused","private prompt");
            Check("transportUsesStdinWithoutShellOrPromptArguments",!start.UseShellExecute&&start.RedirectStandardInput&&start.RedirectStandardOutput&&start.CreateNoWindow&&!start.ArgumentList.Any(x=>x.Contains("private prompt"))&&start.ArgumentList.Contains("--available-tools=__infinity_no_native_tools__")&&start.ArgumentList.Contains("shell")&&start.ArgumentList.Contains("write")&&start.ArgumentList.Contains("read")&&start.ArgumentList.Contains("url")&&start.ArgumentList.Contains("--disable-builtin-mcps")&&start.ArgumentList.Contains("--no-custom-instructions")&&start.ArgumentList.Contains("--sync-session=off")&&!start.ArgumentList.Contains("--allow-all"));
            var transcript=new CliTranscript();var first=transcript.AppendEvent("{\"type\":\"assistant.message_delta\",\"data\":{\"deltaContent\":\"Ready\"}}","copilot");var repeated=transcript.AppendEvent("{\"type\":\"assistant.message\",\"data\":{\"content\":\"Ready\",\"toolRequests\":[]}}","copilot");transcript.AppendEvent("{\"type\":\"result\",\"exitCode\":0}","copilot");
            Check("streamAndSnapshotDoNotDuplicateOutput",first=="Ready"&&repeated==""&&transcript.Text=="Ready"&&transcript.Finish=="stop");
            bool Refuses(string line){try{new CliTranscript().AppendEvent(line,"copilot");return false;}catch(IOException){return true;}}
            Check("nativeToolsAndFailedCompletionsAreRejected",Refuses("{\"type\":\"tool.execution_start\"}")&&Refuses("{\"type\":\"assistant.message\",\"data\":{\"content\":\"\",\"toolRequests\":[{}]}}")&&Refuses("{\"type\":\"result\",\"exitCode\":1}"));
            Check("accountErrorsGiveActionableRedactedMessages",Refuses("{\"type\":\"session.error\",\"data\":{\"message\":\"authentication required\"}}")&&CliAssistant.Failure("authentication required: synthetic-private-data",1).Contains("sign-in is required")&&!CliAssistant.Failure("authentication required: synthetic-private-data",1).Contains("private-data"));
            try{transcript.AppendEvent("{\"type\":\"assistant.message\",\"data\":{\"content\":\"late\"}}","copilot");Check("postCompletionReplayRejected",false);}catch(IOException){Check("postCompletionReplayRejected",true);}
            var launches=new List<System.Diagnostics.ProcessStartInfo>();ModelProfile? selected=null;
            var tools=new AiToolsWindow(settings,launches.Add,useProvider:p=>selected=p);active=tools;tools.Show();tools.UpdateLayout();tools.TestLaunchButtons("copilot");
            Check("toolsPanelLaunchesOnlyWebsiteAndInteractiveCli",tools.LaunchResults.Count==2&&tools.LaunchResults.Values.All(v=>v)&&launches.Any(p=>p.FileName=="https://github.com/copilot")&&launches.Any(p=>p.FileName==exe&&p.UseShellExecute&&p.ArgumentList.Count==0));
            ((FrameworkElement)UiDialogPathTests.Find<TextBlock>(tools).Single(t=>t.Text=="GitHub Copilot").Parent).BringIntoView();await Task.Delay(100);tools.UpdateLayout();
            var copilotButtons=UiDialogPathTests.Find<Button>(tools).Where(b=>System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("GitHub Copilot:")).ToArray();
            Check("copilotButtonsVisibleAfterScrolling",copilotButtons.Length==3&&copilotButtons.All(b=>{var point=b.TranslatePoint(new Point(),tools);return point.Y>=0&&point.Y+b.ActualHeight<=tools.ActualHeight;}));
            QaCapture.Save(tools,Path.ChangeExtension(output,".tools.png"));tools.TestUseProvider("copilot");Check("useInInfinitySelectsCopilotWithoutSending",selected==profile&&!tools.IsVisible);
            var window=new MainWindow([],settings,profile.Id);active=window;window.Show();
            var picker=(ComboBox)typeof(MainWindow).GetField("model",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Check("copilotAppearsInChatPicker",picker.Items.Cast<ModelProfile>().Contains(profile)&&picker.SelectedItem==profile);
            Check("copilotAvailableToAgentAndFallbackRouting",window.AgentModels.Contains(profile)&&((ComboBox)typeof(MainWindow).GetField("fallbackProvider",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Items.Cast<ModelProfile>().Contains(profile));
            var handoff=new CliHandoffWindow(settings.Models,profile.Id,[new("system","Synthetic rules"),new("user","Synthetic request")],"Synthetic handoff");active=handoff;handoff.Show();
            Check("copilotSelectableForReviewedHandoff",handoff.SelectedProvider==profile);handoff.Close();active=window;
            QaCapture.Save(window,Path.ChangeExtension(output,".chat.png"));
            var review=new ProviderConsent(profile,[new("user","synthetic review only")]);active=review;
            _=review.Dispatcher.BeginInvoke(()=>review.Close());var consent=review.ShowDialog();
            Check("closingCloudReviewDoesNotAuthorizeSending",consent!=true);window.Close();active=null;
        }
        catch(Exception e){rows["unexpectedError"]=false;File.WriteAllText(Path.ChangeExtension(output,".error.txt"),e.ToString());active?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Values.All(v=>v)?0:1);
    }
}
