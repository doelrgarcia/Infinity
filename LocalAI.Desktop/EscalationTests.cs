using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task EscalationQa()
    {
        var results=new Dictionary<string,object>();var calls=0;var handoffs=0;var reason="";var response="A normal local answer.";var finish="stop";var fault=false;var slow=false;
        using var listener=new HttpListener();listener.Prefixes.Add("http://127.0.0.1:8031/");
        var testModel=new ModelProfile{Id="isolated-escalation-qa",Label="Synthetic local QA",Engine="ryzen",Endpoint="http://127.0.0.1:8031",ModelPath=Path.GetTempPath(),ContextLimit=4096};
        async Task Serve()
        {
            while(true)
            {
                HttpListenerContext c;try{c=await listener.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
                try
                {
                    var health=c.Request.Url!.AbsolutePath=="/health";var body=health?JsonSerializer.Serialize(new{model_path=testModel.ModelPath,execution_mode="hybrid"}):"";
                    if(!health){calls++;if(slow)await Task.Delay(1800);if(fault)c.Response.StatusCode=503;else body="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=response},finish_reason=finish}}})+"\n\ndata: [DONE]\n\n";}
                    var bytes=Encoding.UTF8.GetBytes(body);await c.Response.OutputStream.WriteAsync(bytes);c.Response.Close();
                }
                catch(Exception e) when(e is HttpListenerException or IOException or ObjectDisposedException){try{c.Response.Abort();}catch(ObjectDisposedException){}}
            }
        }
        var serving=Task.CompletedTask;var reviewedTasks=0;string? retryTask=null;
        try
        {
            listener.Start();serving=Serve();settings.Models.Add(testModel);model.ItemsSource=null;model.ItemsSource=settings.Models;model.SelectedItem=testModel;
            void Fresh(){NewConversation();model.SelectedItem=testModel;maximum.Text="64";contextBudget.Text="4096";prompt.Text="What is 2 plus 3? Respond with only the digit 5. Do not use tools, inspect files or run commands.";}
            handoffTestAction=dialog=>{reviewedTasks=dialog.Packet.Messages.Count(m=>m.Role=="user"&&m.Content==retryTask);handoffs++;reason=Descendants<TextBlock>(dialog).First(t=>t.FontSize==16).Text;results["handoffContextAlwaysFitsExactBridgeLimit"]=dialog.Packet.Prompt.Length<=CliAssistant.MaximumPromptCharacters;results["handoffShowsAllConfiguredProviders"]=Descendants<ComboBox>(dialog).Single().Items.Count==settings.Models.Count(p=>p.IsCloud);results["handoffMessagesAreBoxed"]=dialog.TestMessageCardCount==dialog.Packet.Messages.Count;dialog.UpdateLayout();Capture(dialog,Path.ChangeExtension(smokeOutput!,".handoff.png"));Descendants<Button>(dialog).Single(b=>Equals(b.Content,"Keep local")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));};
            Fresh();results["newChatsDefaultToReviewedCliAssistance"]=conversation.AutoCliFallback&&cliMode.SelectedIndex==0;
            var configured=settings.Models.First(p=>p.IsCloud);fallbackProvider.SelectedItem=configured;cliMode.SelectedIndex=1;var prefs=store.Load(out _).Single(c=>c.Id==conversation.Id);results["fallbackPreferencesPersistLocally"]=!prefs.AutoCliFallback&&prefs.FallbackProviderId==configured.Id;cliMode.SelectedIndex=0;
            Fresh();cliMode.SelectedIndex=1;prompt.Clear();var preferencesOnly=conversation;NewConversation();results["fallbackPreferenceOnlyChatIsRetained"]=conversations.Contains(preferencesOnly);history.SelectedItem=preferencesOnly;results["switchingChatRestoresFallbackPreferences"]=cliMode.SelectedIndex==1&&!conversation.AutoCliFallback;Fresh();
            fallbackProvider.SelectedItem=settings.Models.Last(p=>p.IsCloud);prompt.Clear();var providerOnly=conversation;NewConversation();var keptProvider=conversations.Contains(providerOnly);history.SelectedItem=providerOnly;results["providerPreferenceOnlyChatIsRetainedAndRestored"]=keptProvider&&(fallbackProvider.SelectedItem as ModelProfile)?.Id==providerOnly.FallbackProviderId;Fresh();
            var before=handoffs;await Send();results["successfulLocalAnswerDoesNotEscalate"]=handoffs==before&&conversation.Messages.Last().Provider==testModel.Id;
            Fresh();contextBudget.Text="1024";var original=prompt.Text;before=handoffs;var requests=calls;await Send();results["oversizedLocalContextOpensReviewedHandoffBeforeGeneration"]=handoffs==before+1&&reason.Contains("context budget")&&calls==requests;results["cancelledContextHandoffKeepsTaskAndNoCloudHistory"]=prompt.Text==original&&conversation.Messages.Count==0&&status.Text.Contains("cancelled before sending");
            Fresh();cliMode.SelectedIndex=1;contextBudget.Text="1024";before=handoffs;await Send();results["localOnlyModeNeverEscalatesContextOverflow"]=handoffs==before&&status.Text.StartsWith("Context exceeds")&&prompt.Text.Length>0;
            Fresh();maximum.Text="999999";before=handoffs;await Send();results["invalidGenerationSettingsNeverEscalate"]=handoffs==before&&conversation.Messages.Count==0;
            Fresh();response=CliEscalation.HelpMarker+" I need a stronger assistant to solve this.";before=handoffs;await Send();results["explicitLocalHelpRequestEscalatesOnceAndRetainsAttempt"]=handoffs==before+1&&reason.Contains("requested")&&conversation.Messages.Count==2&&conversation.Messages.Last().Content.StartsWith(CliEscalation.HelpMarker)&&prompt.Text.Length>0;
            Fresh();response="Partial answer";finish="length";before=handoffs;await Send();results["truncatedOutputEscalatesOnce"]=handoffs==before+1&&reason.Contains("output limit")&&conversation.Messages.Last().Content.Contains("Output limit reached");finish="stop";
            Fresh();fault=true;before=handoffs;await Send();results["runtimeFailureEscalatesAndRetainsInterruptedAttempt"]=handoffs==before+1&&reason.Contains("could not finish")&&conversation.Messages.Last().Content.Contains("Interrupted");fault=false;
            Fresh();slow=true;settings.GenerationTimeoutSeconds=1;before=handoffs;await Send();results["localTimeoutEscalates"]=handoffs==before+1&&reason.Contains("timed out");settings.GenerationTimeoutSeconds=600;await Task.Delay(1900);
            Fresh();before=handoffs;var running=Send();await Task.Delay(150);active?.Cancel();await running;results["userCancellationNeverEscalates"]=handoffs==before&&status.Text.Contains("cancelled");await Task.Delay(1900);slow=false;
            Fresh();prompt.Clear();conversation.Messages.Clear();before=handoffs;await AskCli();results["emptyManualHelpHasNoHandoff"]=handoffs==before&&status.Text.Contains("Describe a task");
            Fresh();before=handoffs;await AskCli();results["manualCliHelpOpensReviewWithoutLocalInference"]=handoffs==before+1&&conversation.Messages.Count==0&&prompt.Text.Length>0;
            Fresh();files.Add(new ContextFile(Path.Combine(Path.GetTempPath(),"SyntheticLargeProject.cs"),new string('x',CliAssistant.MaximumPromptCharacters*3)));UpdateContext();var originalContext=BuildMessages(prompt.Text);before=handoffs;var huge=new CliHandoffWindow(settings.Models,conversation.FallbackProviderId,originalContext,"Synthetic large project"){Owner=this};huge.Loaded+=(_,_)=>{results["oversizedProjectReviewShowsExactBoundedExcerpts"]=huge.Packet.OmittedCharacters>CliAssistant.MaximumPromptCharacters&&huge.Packet.Prompt.Contains("excerpt omitted")&&huge.Packet.Prompt.Contains("SyntheticLargeProject.cs")&&huge.Packet.Messages[^1].Content==prompt.Text;results["largeHandoffMessagesAreBoxed"]=huge.TestMessageCardCount==huge.Packet.Messages.Count;huge.UpdateLayout();Capture(huge,Path.ChangeExtension(smokeOutput!,".large-project.png"));huge.Close();};huge.ShowDialog();results["reviewDoesNotTruncateStoredSourceContext"]=files.Single().Content.Length==CliAssistant.MaximumPromptCharacters*3;
            Fresh();response="Original local attempt";await Send();prompt.Clear();var count=conversation.Messages.Count;retryTask=conversation.Messages.First(m=>m.Role=="user").Content;before=handoffs;await AskCli();results["manualRetryKeepsChatWithoutDuplicateUserMessage"]=handoffs==before+1&&conversation.Messages.Count==count;results["manualRetryOutgoingTaskOccursOnce"]=reviewedTasks==1;
            var unavailable=new ModelProfile{Id="missing-cli",Label="Missing synthetic CLI",Engine="codex",Executable=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe")};
            var missing=new CliHandoffWindow([unavailable],unavailable.Id,BuildMessages("test"),"Synthetic availability check"){Owner=this};missing.Loaded+=(_,_)=>{results["missingCliCannotBeConfirmed"]=!Descendants<Button>(missing).Single(b=>Equals(b.Content,"Send reviewed handoff")).IsEnabled;missing.Close();};missing.ShowDialog();
            before=handoffs;var messagesBeforeMissing=conversation.Messages.Count;var draftBeforeMissing=prompt.Text;await ExecuteRequest(unavailable,"Synthetic unavailable CLI",BuildMessages("Synthetic unavailable CLI"),true,false,true);results["missingCliPreflightPreservesDraftAndHistory"]=handoffs==before&&!busy&&conversation.Messages.Count==messagesBeforeMissing&&prompt.Text==draftBeforeMissing&&status.Text.Contains("not found");
            if(Environment.GetEnvironmentVariable("LOCALAI_TEST_CLOUD")=="1")
                foreach(var provider in settings.Models.Where(p=>p.IsCloud).ToArray())
                {
                    Fresh();contextBudget.Text="1024";conversation.FallbackProviderId=provider.Id;var confirmed=0;
                    handoffTestAction=dialog=>{confirmed++;dialog.TestConfirm();};await Send();
                    results[provider.Engine+"RealCliEscalationReturnsAnswerAndPersistsProvider"]=confirmed==1&&conversation.Messages.Count==2&&conversation.Messages.Last().Content.Trim()=="5"&&conversation.Messages.Last().Provider==provider.Id&&conversation.ModelId==testModel.Id&&store.Load(out _).Any(c=>c.Id==conversation.Id&&c.Messages.Last().Provider==provider.Id)&&!busy;
                    results[provider.Engine+"Status"]=status.Text;
                }
            if(Environment.GetEnvironmentVariable("LOCALAI_TEST_CLOUD")=="1")
            {
                Fresh();conversation.FallbackProviderId=configured.Id;response=CliEscalation.HelpMarker+" Need a stronger assistant.";var helpConfirmed=0;handoffTestAction=dialog=>{helpConfirmed++;dialog.TestConfirm();};await Send();results["realCliHelpsAfterLocalAttemptWithoutDuplicatingUserTask"]=helpConfirmed==1&&conversation.Messages.Count==3&&conversation.Messages.Count(m=>m.Role=="user")==1&&conversation.Messages[1].Content.StartsWith(CliEscalation.HelpMarker)&&conversation.Messages[^1].Content.Trim()=="5"&&conversation.Messages[^1].Provider==configured.Id;
                Fresh();conversation.FallbackProviderId=configured.Id;response="A normal local answer.";await Send();prompt.Clear();helpConfirmed=0;await AskCli();results["realManualCliRetryKeepsLocalAnswerAndAddsOnlyHelperAnswer"]=helpConfirmed==1&&conversation.Messages.Count==3&&conversation.Messages.Count(m=>m.Role=="user")==1&&conversation.Messages[1].Content==response&&conversation.Messages[^1].Content.Trim()=="5"&&conversation.Messages[^1].Provider==configured.Id;
                Fresh();contextBudget.Text="1024";conversation.FallbackProviderId=configured.Id;var confirmed=0;handoffTestAction=dialog=>{confirmed++;dialog.TestConfirm();};var cliRunning=Send();await Task.Delay(300);active?.Cancel();await cliRunning;results["realCliCancellationStopsRequestWithoutAnotherHandoff"]=confirmed==1&&!busy&&status.Text.Contains("cancelled")&&conversation.Messages.Last().Content.Contains("Interrupted");
                Fresh();contextBudget.Text="1024";conversation.FallbackProviderId=configured.Id;confirmed=0;settings.GenerationTimeoutSeconds=1;await Send();results["realCliTimeoutDoesNotLoop"]=confirmed==1&&!busy&&status.Text.Contains("timed out");settings.GenerationTimeoutSeconds=600;
            }
            handoffTestAction=null;
            ShowChat();UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".chat.png"));advancedPanel.Visibility=Visibility.Visible;UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".options.png"));
            var maximize=Descendants<Button>(this).Single(b=>Equals(b.Tag,"Maximize / restore"));maximize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var maximized=WindowState==WindowState.Maximized;maximize.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["maximizeAndRestoreButtons"]=maximized&&WindowState==WindowState.Normal;
            Descendants<Button>(this).Single(b=>Equals(b.Tag,"Minimize")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["minimizeButton"]=WindowState==WindowState.Minimized;WindowState=WindowState.Normal;
        }
        catch(Exception e){results["error"]=e.ToString();}
        finally
        {
            handoffTestAction=null;settings.Models.Remove(testModel);
            try{listener.Stop();await serving.WaitAsync(TimeSpan.FromSeconds(10));results["listenerShutdownCompletes"]=true;}
            catch(Exception e){results["shutdownError"]=e.ToString();}
            try{Directory.CreateDirectory(Path.GetDirectoryName(smokeOutput!)!);File.WriteAllText(smokeOutput!,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
            finally{var exitCode=results.Count>0&&results.All(entry=>entry.Value is bool passed?passed:Environment.GetEnvironmentVariable("LOCALAI_TEST_CLOUD")=="1"&&entry.Key.EndsWith("Status",StringComparison.Ordinal)&&entry.Value is string)?0:1;Environment.ExitCode=exitCode;Close();Application.Current.Shutdown(exitCode);}
        }
    }
}
