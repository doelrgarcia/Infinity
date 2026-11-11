using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal static class UiAuxiliaryPathTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"auxiliary-fixture");Directory.CreateDirectory(root);Window? window=null;
        void Check(string key,bool value){rows[key]=value;if(!value)throw new IOException("Auxiliary UI assertion failed: "+key);}
        async Task Wait(Func<bool> predicate){for(var n=0;n<300;n++){if(predicate())return;await Task.Delay(20);}throw new IOException("Auxiliary UI fixture timed out.");}
        Button Button(Window owner,string text)=>UiDialogPathTests.Find<Button>(owner).Single(b=>Equals(b.Content,text)||AutomationProperties.GetName(b)==text);
        TextBox Field(Window owner,string name)=>UiDialogPathTests.Find<TextBox>(owner).Single(t=>AutomationProperties.GetName(t)==name);
        try
        {
            var git=SourceControl.FindTool(SourceProvider.Git)??throw new IOException("Git is required for the isolated source-control fixture.");
            async Task<string> Git(params string[] args)
            {
                var start=new ProcessStartInfo(git){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in new[]{"-c","core.hooksPath="+Path.Combine(root,"no-hooks"),"-c","commit.gpgSign=false","-c","user.name=UI Fixture","-c","user.email=ui@example.invalid"}.Concat(args))start.ArgumentList.Add(arg);
                using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var text=await stdout;var error=await stderr;if(process.ExitCode!=0)throw new IOException(error);return text;
            }
            await Git("init","-b","main");var file=Path.Combine(root,"Example.txt");File.WriteAllText(file,"Original synthetic text\n");await Git("add","Example.txt");await Git("commit","-m","Synthetic initial state");File.WriteAllText(file,"Changed synthetic text\n");
            var launches=new List<ProcessStartInfo>();string? copied=null;var source=new SourceControlWindow([root],root,file,launches.Add,text=>copied=text);window=source;source.Show();
            var trust=UiDialogPathTests.Find<CheckBox>(source).Single(c=>c.Content is string s&&s.StartsWith("I trust"));
            Check("sourceSelectorsExposeDistinctAccessibleNames",UiDialogPathTests.Find<ComboBox>(source).Any(c=>AutomationProperties.GetName(c)=="Source control provider")&&UiDialogPathTests.Find<ComboBox>(source).Any(c=>AutomationProperties.GetName(c)=="Repository operation"));
            async Task Click(string text){var button=Button(source,text);UiPathTests.Click(button);await Wait(()=>Button(source,"status").IsEnabled);}
            await Click("status");Check("sourceStatusRequiresExplicitTrust",source.TestOutput.Length==0&&UiDialogPathTests.Find<TextBlock>(source).Any(t=>t.Text.Contains("Confirm repository")));
            UiPathTests.Click(trust);await Click("status");Check("sourceStatusButtonReadsActualWorkingCopy",source.TestOutput.Contains("Example.txt"));
            await Click("diff");Check("sourceDiffButtonShowsActualChangedLines",source.TestOutput.Contains("Changed synthetic text"));
            await Click("history");Check("sourceHistoryButtonReadsActualCommit",source.TestOutput.Contains("Synthetic initial state"));
            await Click("branches");Check("sourceBranchesButtonListsCurrentBranch",source.TestOutput.Contains("main"));
            await Click("Copy output");Check("sourceCopyOutputButtonCopiesDisplayedText",copied==source.TestOutput);
            await Click("Preview repository action");Check("sourcePreviewButtonEnablesConfirmation",source.TestCanApply);
            var operation=UiDialogPathTests.Find<ComboBox>(source).Single(c=>c.Items.OfType<string>().Contains("stage"));operation.SelectedItem="unstage";Check("sourceOperationEditRevokesPreview",!source.TestCanApply);operation.SelectedItem="stage";
            await Click("Preview repository action");await Click("Confirm reviewed repository action");Check("sourceApplyButtonStagesActualFile",!source.TestCanApply);

            Check("sourceStageHasActualIndexChange",(await Git("diff","--cached")).Contains("Changed synthetic text"));
            var provider=UiDialogPathTests.Find<ComboBox>(source).Single(c=>c.Items.OfType<SourceProvider>().Any());
            foreach(var kind in Enum.GetValues<SourceProvider>())
            {
                provider.SelectedItem=kind;await Click("Official client setup");Check("officialSetupDispatch_"+kind,launches.Last().FileName==SourceControl.SetupUrl(kind));
            }
            await Click("Azure DevOps web");Check("azureWebButtonUsesOfficialDestination",launches.Last().FileName=="https://dev.azure.com");
            UiDialogPathTests.Find<TabControl>(source).Single().SelectedIndex=1;source.UpdateLayout();
            var draft=Field(source,"Reviewed Teams draft");draft.Text="Synthetic reviewed team update";
            var recipients=UiDialogPathTests.Find<TextBox>(source).Single(t=>t.IsVisible&&!t.IsReadOnly&&AutomationProperties.GetName(t)!="Reviewed Teams draft");recipients.Text="qa@example.invalid";
            var teamsConsent=UiDialogPathTests.Find<CheckBox>(source).Single(c=>c.IsVisible);var beforeLaunch=launches.Count;
            UiPathTests.Click(Button(source,"Open reviewed Teams draft"));Check("teamsDraftButtonRequiresConsent",launches.Count==beforeLaunch);
            UiPathTests.Click(teamsConsent);UiPathTests.Click(Button(source,"Open reviewed Teams draft"));Check("teamsDraftButtonDispatchesExactReviewedFields",launches.Count==beforeLaunch+1&&Uri.UnescapeDataString(launches.Last().FileName).Contains(draft.Text)&&launches.Last().FileName.Contains("qa%40example.invalid"));
            draft.Text+=" changed";Check("teamsDraftEditRevokesConsent",teamsConsent.IsChecked==false);
            UiPathTests.Click(Button(source,"Copy draft"));Check("teamsCopyDraftUsesOnlyDraft",copied==draft.Text);
            UiPathTests.Click(Button(source,"Open Teams desktop / web"));Check("teamsHomeButtonDispatchesOfficialHome",launches.Last().FileName is "msteams:" or "https://teams.microsoft.com");source.Close();window=null;

            var id=Guid.NewGuid();var other=Guid.NewGuid();var snapshots=new List<ChatOverview>{new(id,"Active chat","Working",false,2,DateTimeOffset.UtcNow,true,true,"Synthetic live work"),new(other,"Flagged chat","Needs attention",true,1,DateTimeOffset.UtcNow,false,false,"Synthetic attention")};
            var actions=new List<(string Kind,Guid? Id,string Text)>();var blocked=false;
            var assistant=new ChatAssistantWindow(()=>snapshots,selected=>"PREVIEW_"+selected,(kind,selected,value)=>
            {
                actions.Add((kind,selected,value));var index=snapshots.FindIndex(r=>r.Id==selected);
                if(kind=="rename")snapshots[index]=snapshots[index] with {Title=value};
                if(kind=="flag")snapshots[index]=snapshots[index] with {NeedsAttention=!snapshots[index].NeedsAttention};
                if(kind=="stop")snapshots[index]=snapshots[index] with {CanStop=false,State="Stopped"};
                if(kind=="new")snapshots.Add(new(Guid.NewGuid(),value,"Ready",false,0,null,false,false,""));
                return Task.CompletedTask;
            },()=>blocked);window=assistant;assistant.Show();
            var chats=UiDialogPathTests.Find<ListBox>(assistant).Single();var name=Field(assistant,"Chat name");
            Check("chatAssistantInitiallySelectsCurrentChat",chats.SelectedItem is ChatOverview {IsCurrent:true}&&Field(assistant,"Selected chat preview").Text=="PREVIEW_"+id);
            UiPathTests.Click(Button(assistant,"Open chat"));Check("chatAssistantOpenButtonUsesSelectedId",actions.Last() is {Kind:"open",Id:var openedId}&&openedId==id);
            name.Text="Renamed via activity";UiPathTests.Click(Button(assistant,"Rename"));Check("chatAssistantRenameButtonUsesEnteredName",snapshots[0].Title==name.Text);
            UiPathTests.Click(Button(assistant,"Toggle chat attention"));Check("chatAssistantFlagButtonUpdatesSelectedChat",snapshots[0].NeedsAttention);
            UiPathTests.Click(Button(assistant,"Toggle chat attention"));Check("chatAssistantAcknowledgeButtonClearsFlag",!snapshots[0].NeedsAttention);
            UiPathTests.Click(Button(assistant,"Suggest next steps"));Check("chatAssistantSuggestButtonDispatchesSelectedChat",actions.Last().Kind=="suggest"&&actions.Last().Id==id);
            UiPathTests.Click(Button(assistant,"Stop active response"));Check("chatAssistantStopButtonDisablesAfterStop",!snapshots[0].CanStop&&!Button(assistant,"Stop active response").IsEnabled);
            var filter=UiDialogPathTests.Find<CheckBox>(assistant).Single();UiPathTests.Click(filter);Check("chatAssistantAttentionCheckboxFiltersRows",chats.Items.Count==1&&chats.Items.OfType<ChatOverview>().All(c=>c.NeedsAttention));UiPathTests.Click(filter);
            Field(assistant,"Find chats").Text="Flagged";Check("chatAssistantSearchFiltersRows",chats.Items.Count==1&&chats.Items.OfType<ChatOverview>().Single().Id==other);Field(assistant,"Find chats").Clear();
            name.Text="New activity chat";UiPathTests.Click(Button(assistant,"New chat"));Check("chatAssistantNewButtonUsesEnteredName",snapshots.Last().Title==name.Text&&actions.Last().Kind=="new");
            blocked=true;assistant.TestRefresh();Check("chatAssistantWorkingStateBlocksChatMutation",!Button(assistant,"New chat").IsEnabled&&!Button(assistant,"Rename").IsEnabled&&!Button(assistant,"Open chat").IsEnabled);
            assistant.Close();window=null;

            foreach(var engine in new[]{"codex","cursor","grok","deepseek"})
            {
                var fixtureCli=Path.Combine(root,"fixture.cmd");File.WriteAllText(fixtureCli,"@exit /b 0");
                var settings=new Settings{Models=new[]{"codex","cursor","grok","deepseek"}.Select(e=>new ModelProfile{Id=e,Engine=e,Executable=Environment.ProcessPath!,ModelPath=Environment.ProcessPath!}).ToList()};ModelProfile? selected=null;
                var tools=new AiToolsWindow(settings,_=>throw new IOException("Provider selection must not launch anything"),()=>fixtureCli,p=>selected=p);window=tools;tools.Show();
                var use=UiDialogPathTests.Find<Button>(tools).Single(b=>AutomationProperties.GetName(b).StartsWith(engine=="codex"?"ChatGPT":engine=="deepseek"?"DeepSeek":char.ToUpperInvariant(engine[0])+engine[1..])&&AutomationProperties.GetName(b).EndsWith(": Use in Infinity"));
                Check("providerSelectButtonEnabled_"+engine,use.IsEnabled);UiPathTests.Click(use);Check("providerSelectButtonReturnsExactProfileWithoutSending_"+engine,selected?.Engine==engine&&!tools.IsVisible);window=null;
            }
            var saveDraft=new CodeSaveWindow("class SyntheticDraft {}","cs");window=saveDraft;saveDraft.Show();Check("codeSaveFieldsExposeExactAccessibleRoles",Field(saveDraft,"Save destination folder")!=null&&Field(saveDraft,"Save file name").Text=="Generated.cs"&&Field(saveDraft,"Current file contents").IsReadOnly&&Field(saveDraft,"Proposed file contents").Text=="class SyntheticDraft {}");
            var priorPicker=FolderPicker.TestSelection;
            try
            {
                var chooseFolder=UiDialogPathTests.Find<Button>(saveDraft).Single(b=>AutomationProperties.GetName(b)=="Choose folder");
                var originalFolder=Field(saveDraft,"Save destination folder").Text;
                FolderPicker.TestSelection=_=>null;UiPathTests.Click(chooseFolder);Check("codeSaveCancelKeepsDestination",Field(saveDraft,"Save destination folder").Text==originalFolder);
                FolderPicker.TestSelection=_=>root;UiPathTests.Click(chooseFolder);Check("codeSaveChooseFolderButtonReturnsExactPath",Field(saveDraft,"Save destination folder").Text==root);
            }
            finally{FolderPicker.TestSelection=priorPicker;}
            _=saveDraft.Dispatcher.BeginInvoke(()=>{var confirmation=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==saveDraft&&w.Title=="Synthetic cancellation"&&w.IsVisible);UiPathTests.Click(Button(confirmation,"Cancel"));});
            Check("genericReviewCancelButtonReturnsDeclined",!AgentView.Confirm(saveDraft,"Synthetic cancellation","No real action is connected to this fixture."));saveDraft.Close();window=null;
            var debugProject=Path.Combine(root,"Synthetic.csproj");File.WriteAllText(debugProject,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var debug=new DebugWindow([root]);window=debug;debug.Show();Field(debug,"Debug project path").Clear();UiPathTests.Click(Button(debug,"Find project in folder"));Check("debugFindProjectButtonFindsActualConnectedProject",debug.ProjectPath==debugProject);debug.Close();window=null;
            var repairSettings=new Settings{Models=[new(){Id="fixture-codex",Engine="codex",Executable=Environment.ProcessPath!,ModelPath=Environment.ProcessPath!}]};var repairLaunches=0;
            var toolsPanel=new AiToolsWindow(repairSettings,_=>repairLaunches++);window=toolsPanel;toolsPanel.Show();var repairDone=new TaskCompletionSource();Exception? repairFailure=null;
            _=toolsPanel.Dispatcher.BeginInvoke(async()=>
            {
                DependencySetupWindow? repair=null;
                try
                {
                    repair=Application.Current.Windows.OfType<DependencySetupWindow>().Single(w=>w.Owner==toolsPanel&&w.IsVisible);await Wait(()=>!repair.TestBusy);Check("toolsRepairButtonOpensRepairMode",UiDialogPathTests.Find<Button>(repair).Any(b=>Equals(b.Content,"Show missing only")));repair.Close();
                }
                catch(Exception error){repairFailure=error;repair?.Close();}
                finally{repairDone.TrySetResult();}
            });
            UiPathTests.Click(Button(toolsPanel,"Repair managed downloads"));await repairDone.Task.WaitAsync(TimeSpan.FromSeconds(10));if(repairFailure!=null)throw repairFailure;
            Check("cancelToolsRepairDoesNotLaunchProviderOrInstall",repairLaunches==0&&UiDialogPathTests.Find<TextBlock>(toolsPanel).Any(t=>t.Text.Contains("Repair closed")));toolsPanel.Close();window=null;
            var syntheticMessages=new[]{new Message("system","Synthetic UI test instructions."),new Message("user","SYNTHETIC_REVIEW_ONLY")};
            var syntheticProvider=new ModelProfile{Id="synthetic-codex",Label="Synthetic Codex",Engine="codex",Executable=Environment.ProcessPath!,ModelPath=Environment.ProcessPath!};
            var consent=new ProviderConsent(syntheticProvider,syntheticMessages);window=consent;consent.Loaded+=(_,_)=>UiPathTests.Click(Button(consent,"Send this reviewed context to cloud provider"));
            Check("standaloneCloudReviewButtonReturnsExactReviewedMessages",consent.ShowDialog()==true&&consent.ReviewedMessages.SequenceEqual(syntheticMessages));window=null;
            var handoff=new CliHandoffWindow([syntheticProvider],syntheticProvider.Id,syntheticMessages,"Synthetic review only");window=handoff;
            handoff.Loaded+=(_,_)=>
            {
                var details=UiDialogPathTests.Find<System.Windows.Controls.Primitives.ToggleButton>(handoff).Single(b=>Equals(b.Content,"Review request details"));UiPathTests.Click(details);Check("handoffDetailsToggleExpandsReviewedPacket",handoff.TestRequestDetails.IsExpanded);UiPathTests.Click(details);Check("handoffDetailsToggleCollapsesPacket",!handoff.TestRequestDetails.IsExpanded);UiPathTests.Click(Button(handoff,"Send reviewed handoff"));
            };Check("standaloneHandoffButtonReturnsSelectedProviderAndPacket",handoff.ShowDialog()==true&&handoff.SelectedProvider==syntheticProvider&&handoff.Packet.Messages.Any(m=>m.Content.Contains("SYNTHETIC_REVIEW_ONLY")));window=null;
            var cancelledHandoff=new CliHandoffWindow([syntheticProvider],syntheticProvider.Id,syntheticMessages,"Synthetic cancel only");window=cancelledHandoff;cancelledHandoff.Loaded+=(_,_)=>UiPathTests.Click(Button(cancelledHandoff,"Keep local"));Check("handoffKeepLocalButtonReturnsCancelled",cancelledHandoff.ShowDialog()==false);window=null;
            var unavailable=new CliHandoffWindow([new(){Id="missing",Engine="codex",Executable=Path.Combine(root,"missing.exe")}],"missing",syntheticMessages,"Synthetic missing CLI");window=unavailable;unavailable.Loaded+=(_,_)=>{Check("handoffMissingCliDisablesSendButton",!Button(unavailable,"Send reviewed handoff").IsEnabled);UiPathTests.Click(Button(unavailable,"Keep local"));};unavailable.ShowDialog();window=null;
            // These standalone dialogs have no caller that sends a request.
            // Only their returned decision and exact packet are being tested.
            var research=new ResearchWindow();window=research;research.Show();
            UiPathTests.Click(Button(research,"Open selected source in browser"));Check("researchOpenWithoutSelectionShowsActionableMessage",UiDialogPathTests.Find<TextBlock>(research).Any(t=>t.Text=="Select a search result first."));
            UiPathTests.Click(Button(research,"Open this search in browser"));Check("researchOpenEmptySearchShowsActionableMessage",UiDialogPathTests.Find<TextBlock>(research).Any(t=>t.Text.Contains("1–500")));
            await research.TestSearch("");Check("researchSearchEmptyQueryShowsErrorAndRestoresButton",Button(research,"Search web").IsEnabled&&research.IsVisible&&UiDialogPathTests.Find<TextBlock>(research).Any(t=>t.Text.Contains("query",StringComparison.OrdinalIgnoreCase)));
            Field(research,"Public source URL").Text="http://127.0.0.1/private";UiPathTests.Click(Button(research,"Include public URL"));await Task.Delay(20);Check("researchIncludePrivateUrlRefusesWithoutClosing",research.IsVisible&&research.Sources.Count==0&&Button(research,"Include public URL").IsEnabled&&UiDialogPathTests.Find<TextBlock>(research).Any(t=>t.Text.Contains("HTTPS")));
            Check("researchFieldsExposeAccessibleNames",Field(research,"Web search query")!=null&&UiDialogPathTests.Find<ListBox>(research).Any(l=>AutomationProperties.GetName(l)=="Web search results"));research.Close();window=null;
            var goal=new GoalWindow(null,"");window=goal;goal.Loaded+=(_,_)=>
            {
                try
                {
                    UiPathTests.Click(Button(goal,"Start goal"));Check("emptyGoalStaysOpen",goal.IsVisible&&goal.Goal.Objective.Length==0);
                    Field(goal,"Goal").Text="Synthetic goal";Field(goal,"Done when").Text="Synthetic result reviewed";Field(goal,"Turn budget (1–100)").Text="101";
                    UiPathTests.Click(Button(goal,"Start goal"));Check("invalidGoalBudgetStaysOpen",goal.IsVisible&&goal.Goal.Objective.Length==0);
                    Field(goal,"Turn budget (1–100)").Text="2";UiPathTests.Click(Button(goal,"Start goal"));Check("validGoalButtonReturnsExactGoal",!goal.IsVisible&&goal.Goal is {Objective:"Synthetic goal",TurnBudget:2});
                }
                catch(Exception error){rows["goalError"]=error.ToString();goal.Close();}
            };goal.ShowDialog();window=null;

        }
        catch(Exception error){rows["error"]=error.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==60&&rows.Values.All(v=>v is true)?0:1);
    }
}
