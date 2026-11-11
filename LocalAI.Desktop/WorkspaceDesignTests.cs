using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task WorkspaceQa()
    {
        var results=new Dictionary<string,object>();
        try {store.Load(out var warning);if(warning!="")results["startupReadWarningIsVisible"]=status.Text==warning;await ExerciseWorkspaceDesign(results);}catch(Exception e){results["error"]=e.ToString();}
        File.WriteAllText(smokeOutput!,System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));var exitCode=results.Count>0&&results.Values.All(v=>v is bool passed&&passed)?0:1;Environment.ExitCode=exitCode;Close();Application.Current.Shutdown(exitCode);
    }
    private async Task ExerciseWorkspaceDesign(Dictionary<string,object> results)
    {
        NewConversation();UpdateLayout();
        var pickerTarget=new TextBox{Text="unchanged"};var pickerCalls=0;
        var pickerButton=FolderPicker.Button(this,pickerTarget,onChosen:_=>pickerCalls++);
        var priorPicker=FolderPicker.TestSelection;
        try
        {
            FolderPicker.TestSelection=_=>null;pickerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            results["folderPickerCancelPreservesTextAndDoesNotNavigate"]=pickerTarget.Text=="unchanged"&&pickerCalls==0;
            FolderPicker.TestSelection=_=>Path.GetTempPath();pickerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            results["folderPickerSelectionUpdatesTextAndNotifiesOnce"]=pickerTarget.Text==Path.GetTempPath()&&pickerCalls==1;
            var appendPicker=FolderPicker.Button(this,pickerTarget,append:true);appendPicker.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            results["folderPickerAppendPreservesExistingRoots"]=pickerTarget.Text==Path.GetTempPath().TrimEnd()+Environment.NewLine+Path.GetTempPath();
        }
        finally{FolderPicker.TestSelection=priorPicker;}

        Capture(this,Path.ChangeExtension(smokeOutput!,".simple-home.png"));
        conversation.Messages.Add(new("user","Can you help me plan a small project?"));conversation.Messages.Add(new("assistant","Yes. Tell me what you want to build and what a successful result looks like. We can work through it one step at a time."));Render();UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".simple-conversation.png"));conversation.Messages.Clear();Render();
        for(var i=0;i<24;i++)conversation.Messages.Add(new("assistant","Scroll fixture "+i+": "+new string('x',400)));Render();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Loaded);results["submittedConversationScrollsToLatestMessage"]=scroll.ScrollableHeight>0&&scroll.VerticalOffset>=scroll.ScrollableHeight-2;conversation.Messages.Clear();Render();
        results["simpleChatStartsWithoutWorkspaceOrModeTabs"]=inspectorColumn.Width.Value==0&&!chatTab.IsVisible&&!codeTab.IsVisible&&!agentTab.IsVisible&&chatView.IsVisible;
        results["modelPickerIsOutsideComposer"]=model.IsVisible&&!Descendants<ComboBox>(chatView).Contains(model);
        var tools=Descendants<Button>(this).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Open Infinity tools");
        var add=Descendants<Button>(this).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Add files or web sources");
        tools.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);tools.ContextMenu.UpdateLayout();QaCapture.Save(tools.ContextMenu,Path.ChangeExtension(smokeOutput!,".tools-menu.png"));var toolsOpened=tools.ContextMenu.IsOpen&&tools.ContextMenu.ActualWidth>0;tools.ContextMenu.IsOpen=false;
        results["toolsKeepTasksCodeBrowserAndSettingsReachable"]=toolsOpened&&new[]{"Tasks and approvals","Code workspace","Browser","Debug code","AI apps and settings","Ask a stronger AI"}.All(name=>tools.ContextMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,name)));
        add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);add.ContextMenu.UpdateLayout();QaCapture.Save(add.ContextMenu,Path.ChangeExtension(smokeOutput!,".add-menu.png"));var addOpened=add.ContextMenu.IsOpen&&add.ContextMenu.ActualWidth>0;add.ContextMenu.IsOpen=false;
        results["attachmentMenuKeepsFileFolderAndWebActions"]=addOpened&&new[]{"Add files","Add folder","Search web or add URL","Review attached files","Review web sources"}.All(name=>add.ContextMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,name)));
        void Choose(Button button,string label)=>button.ContextMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        prompt.Text="Keep this simple-chat draft";var messagesBeforeMenu=conversation.Messages.Count;
        Choose(add,"Review attached files");UpdateLayout();results["attachmentReviewRevealsWorkspaceWithoutSending"]=inspectorContent.IsVisible&&inspectorContent.Content==filesInspector&&conversation.Messages.Count==messagesBeforeMenu;ToggleInspector();
        Choose(tools,"Chat options");UpdateLayout();results["optionsAreAvailableOnDemandWithoutLosingDraft"]=responseStyle.IsVisible&&advancedPanel.IsVisible&&prompt.Text=="Keep this simple-chat draft"&&conversation.Messages.Count==messagesBeforeMenu;Choose(tools,"Chat options");
        Choose(tools,"Code workspace");UpdateLayout();var codeReachable=codeView.IsVisible&&returnToChat.IsVisible;returnToChat.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));UpdateLayout();results["toolsCodeAndBackRetainDraftAndCollapsedWorkspace"]=codeReachable&&chatView.IsVisible&&!returnToChat.IsVisible&&prompt.Text=="Keep this simple-chat draft"&&inspectorColumn.Width.Value==0;prompt.Clear();
        results["voicePromptButtonAvailable"]=System.Windows.Automation.AutomationProperties.GetName(voiceMic)=="Dictate prompt";
        prompt.Text="Existing draft.";prompt.Select(prompt.Text.Length,0);var voiceCount=conversation.Messages.Count;
        var voiceFake=new TestDictation();voiceSessionFactory=()=>voiceFake;ToggleDictation();voiceFake.Say("Discarded speech");CancelDictation();results["voiceCancelPreservesDraft"]=prompt.Text=="Existing draft.";
        voiceFake=new();voiceSessionFactory=()=>voiceFake;ToggleDictation();voiceFake.Say("Add tests.");ToggleDictation();voiceSessionFactory=null;results["voiceInsertsEditableTextWithoutSending"]=prompt.Text=="Existing draft. Add tests."&&conversation.Messages.Count==voiceCount&&!prompt.IsReadOnly;prompt.Clear();

        results["fileWorkspaceButtonIsAvailable"]=Descendants<Button>(this).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Files on computer and network");
        results["commandRunnerEntryIsAvailable"]=Descendants<Button>(this).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Review and run commands");
        conversation.WorkspaceRoots.Add(Path.GetTempPath());
        results["connectedWorkspaceExplainsCommandProposals"]=BuildMessages("Build the project")[0].Content.Contains("infinity-command");
        var commandPacket=System.Text.Json.JsonSerializer.Serialize(new CommandRequest("Write-Output 'not automatically executed'",Path.GetTempPath()),new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase});
        conversation.Messages.Add(new("assistant","```infinity-command\n"+commandPacket+"\n```"));Render();results["assistantCommandProposalHasReviewButton"]=CodeButtons("Review command").Count()==1;
        conversation.Messages.Clear();conversation.Messages.Add(new("user","```infinity-command\n"+commandPacket+"\n```"));Render();results["userQuotedCommandsHaveNoExecuteButton"]=!CodeButtons("Review command").Any();conversation.Messages.Clear();conversation.WorkspaceRoots.Clear();Render();
        conversation.WorkspaceRoots.Add(Path.GetTempPath());results["connectedRootsEnableReviewedFileProtocol"]=BuildMessages("List files")[0].Content.Contains("infinity-files")&&!BuildMessages("List files")[0].Content.Contains("Do not execute commands, use tools");
        conversation.Messages.Add(new("assistant","```infinity-files\n[{\"operation\":\"list\",\"path\":\"example\"}]\n```"));Render();results["assistantFileRequestsHaveReviewButton"]=CodeButtons("Review AI file actions").Count()==1;conversation.Messages.Clear();conversation.WorkspaceRoots.Clear();Render();
        conversation.WorkspaceRoots.Add(Path.GetTempPath());conversation.Messages.Add(new("assistant","```json\n[{\"operation\":\"list\",\"path\":\"example\"}]\n```"));Render();results["strictJsonFileRequestsHaveReviewButton"]=CodeButtons("Review AI file actions").Count()==1;conversation.Messages.Clear();conversation.WorkspaceRoots.Clear();Render();
        SelectWorkingFile(Path.Combine(Path.GetTempPath(),"stale-source.cs"),true);results["staleSourceSelectionDoesNotThrowOrSelectAnotherFile"]=selectedSourcePath==null&&status.Text.Contains("no longer included");
        var missingProfile=new ModelProfile{Id="qa-missing-cloud",Engine="codex",Executable=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe")};var priorDraft=prompt.Text;prompt.Text="Retain this draft";var priorCount=conversation.Messages.Count;await ExecuteRequest(missingProfile,prompt.Text,BuildMessages(prompt.Text),true,true);results["cloudPreflightRetainsDraftBeforeConsent"]=prompt.Text=="Retain this draft"&&conversation.Messages.Count==priorCount&&status.Text.Contains("not found");prompt.Text=priorDraft;

        Capture(this,Path.ChangeExtension(smokeOutput!,".welcome.png"));
        results["askCliIsOutsideComposerToolbar"]=LogicalTreeHelper.GetParent(askCli) is DockPanel helpRow&&LogicalTreeHelper.GetParent(helpRow) is StackPanel activity&&LogicalTreeHelper.GetParent(activity)==chatView;
        results["friendlyWelcomeExplainsNextStep"]=Descendants<TextBlock>(transcript).Any(t=>t.Text=="Ask a question or describe what you want to do.");
        results["filesEmptyStateExplainsNextStep"]=Descendants<TextBlock>(workingFiles).Any(t=>t.Text=="No files yet");
        results["workspaceEditorsHaveAccessibleNames"]=System.Windows.Automation.AutomationProperties.GetName(model)=="Model"&&System.Windows.Automation.AutomationProperties.GetName(responseStyle)=="Response style"&&System.Windows.Automation.AutomationProperties.GetName(codeEditor)=="Code draft editor";
        var starters=Descendants<Button>(transcript).Where(b=>b.Tag is string tag&&tag.StartsWith("Starter:")).ToArray();results["welcomeStartersPresent"]=starters.Length==4;
        starters[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["starterDraftDoesNotSend"]=prompt.Text.Contains("feature")&&conversation.Messages.Count==0;prompt.Clear();
        var title="Design QA "+Guid.NewGuid().ToString("N")[..8];conversation.Title=title;history.Items.Refresh();
        SelectInspector("Task");briefEditor.Text="Build an Acme demo. Keep the UI dark. Require reviewed file saves.";await Task.Delay(800);FlushBrief();responseStyle.SelectedItem="Plan";
        results["taskBriefAndStylePersist"]=store.Load(out _).Any(c=>c.Id==conversation.Id&&c.TaskBrief==briefEditor.Text&&c.ResponseStyle=="Plan");
        results["taskBriefAndStyleEnterRequest"]=BuildMessages("test").Any(m=>m.Content.Contains(briefEditor.Text))&&BuildMessages("test").Any(m=>m.Content.Contains("implementation plan"));
        var priorModel=model.SelectedItem;var reviewedBrief=true;var cloudUnits=true;var messageCount=conversation.Messages.Count;
        foreach(var provider in settings.Models.Where(m=>m.IsCloud))
        {
            var originalExecutable=provider.Executable;var originalModelPath=provider.ModelPath;provider.Executable=typeof(MainWindow).Assembly.Location;provider.ModelPath=provider.Executable;model.SelectedItem=provider;cloudUnits&=context.Text.Contains($"{CliAssistant.MaximumPromptCharacters:N0}")&&context.Text.Contains("characters")&&!context.Text.Contains("GPU token count");prompt.Text="Synthetic task-context preview. Cancel before sending.";
            _=Dispatcher.BeginInvoke(new Action(()=>{var consent=Application.Current.Windows.OfType<ProviderConsent>().Single();var outgoing=Descendants<TextBox>(consent).Single().Text;reviewedBrief&=outgoing.Contains(briefEditor.Text)&&outgoing.Contains("implementation plan");consent.UpdateLayout();Capture(consent,Path.ChangeExtension(smokeOutput!,".task-consent.png"));consent.Close();}));
            await Send();provider.Executable=originalExecutable;provider.ModelPath=originalModelPath;reviewedBrief&=conversation.Messages.Count==messageCount&&status.Text.Contains("cancelled before sending");
        }
        model.SelectedItem=priorModel;prompt.Clear();results["taskAndStyleVisibleInAllCloudReviewsCancelSendsNothing"]=reviewedBrief;results["cloudModelSelectionShowsCharacterUnitsBeforeSend"]=cloudUnits;
        briefEditor.Clear();FlushBrief();results["clearingBriefPersists"]=store.Load(out _).Single(c=>c.Id==conversation.Id).TaskBrief=="";
        briefEditor.Text="Implement and verify a focused change.";FlushBrief();UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".task.png"));var taskChat=conversation;
        prompt.Text="Unsent prompt stays with this chat.";NewConversation();results["newChatStartsWithEmptyPrompt"]=prompt.Text=="";historySearch.Text=title;results["historySearchFiltersLocally"]=history.Items.Count==1;history.SelectedItem=taskChat;
        results["chatRestoresBriefStyleAndPrompt"]=briefEditor.Text==taskChat.TaskBrief&&responseStyle.SelectedItem as string=="Plan"&&prompt.Text=="Unsent prompt stays with this chat.";historySearch.Clear();prompt.Clear();
        conversation.Messages.Add(new("assistant","```csharp\npublic class DraftExample {}\n```"));Render();ShowChat();CodeButtons("Open code").Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        results["codeBlockOpensEditableWorkspace"]=codeView.Visibility==Visibility.Visible&&!codeEditor.IsReadOnly&&codeEditor.Text.Contains("DraftExample");
        codeEditor.Text="public class DraftExample { public int Value => 42; }";var edited=codeEditor.Text;OpenCode("second snippet","text",null,false);OpenCode("public class DraftExample {}","csharp",null,false);results["switchingCodeKeepsEdits"]=codeEditor.Text==edited;
        Descendants<Button>(codeView).Single(b=>Equals(b.Content,"Copy")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["copyEditorUsesCurrentEdits"]=Clipboard.ContainsText()&&Clipboard.GetText()==edited;
        if(results["copyEditorUsesCurrentEdits"] is false)results["copyEditorFailureStatus"]=status.Text;
        var directory=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"design-fixture");Directory.CreateDirectory(directory);var sourcePath=Path.Combine(directory,"Original.cs");File.WriteAllText(sourcePath,"public class Original {}");files.Add(ContextFiles.Read(sourcePath));UpdateContext();SelectInspector("Files");workingFiles.TestSelect(sourcePath);
        results["sourcePreviewIsReadOnly"]=codeEditor.IsReadOnly&&codeEditor.Text=="public class Original {}";editorDraft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));codeEditor.Text="public class Original { public bool Ready => true; }";edited=codeEditor.Text;
        var folderButton=Descendants<Button>(workingFiles).First(b=>System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Folder: "));var folderRows=(StackPanel)LogicalTreeHelper.GetParent(folderButton);var members=(StackPanel)folderRows.Children[folderRows.Children.IndexOf(folderButton)+1];folderButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var collapsed=members.Visibility==Visibility.Collapsed;folderButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["folderRowsCollapseAndExpand"]=collapsed&&members.Visibility==Visibility.Visible;
        var cancelledName="Cancelled-"+Guid.NewGuid().ToString("N")+".cs";saveTestAction=window=>{window.TestPreview(directory,cancelledName);window.Close();};editorSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));saveTestAction=null;results["editorSaveCancelPreservesDraftAndDisk"]=!File.Exists(Path.Combine(directory,cancelledName))&&File.ReadAllText(sourcePath)=="public class Original {}"&&codeEditor.Text==edited;
        saveTestAction=window=>{window.TestPreview(directory,"ReviewedDraft.cs");window.TestSave();};editorSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));saveTestAction=null;
        results["editorReviewedSaveUpdatesFileAndPanel"]=File.ReadAllText(Path.Combine(directory,"ReviewedDraft.cs"))==edited&&workingFiles.DisplayedPaths.Contains(Path.Combine(directory,"ReviewedDraft.cs"))&&File.ReadAllText(sourcePath)=="public class Original {}";
        workingFiles.TestSelect(sourcePath);results["savedDraftDoesNotReplaceOriginalPreview"]=codeEditor.IsReadOnly&&codeEditor.Text=="public class Original {}";
        editorDraft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));codeEditor.Text="public class Original { public bool Reviewed => true; }";edited=codeEditor.Text;
        saveTestAction=window=>{window.TestPreview(directory,"Original.cs");window.TestSave();};editorSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));saveTestAction=null;
        results["reviewedSourceOverwriteRefreshesIncludedContext"]=File.ReadAllText(sourcePath)==edited&&files.Single(f=>f.Path==sourcePath).Content==edited;
        UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".editor.png"));
        codeEditor.Text="public class Original { public string Password = \"synthetic-test-only\"; }";
        saveTestAction=window=>{window.TestPreview(directory,"Original.cs");window.TestSave();};editorSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));saveTestAction=null;
        results["unsafeSavedSourceExcludedFromFutureContext"]=files.All(f=>f.Path!=sourcePath)&&BuildMessages("test").All(m=>!m.Content.Contains("synthetic-test-only"))&&status.Text.Contains("source context excluded");
        codeEditor.Text=edited;saveTestAction=window=>{window.TestPreview(directory,"Original.cs");window.TestSave();};editorSave.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));saveTestAction=null;
        ToggleNavigation();results["chatPaneCollapses"]=navigationColumn.Width.Value==0;ToggleNavigation();if(inspectorColumn.Width.Value==0)ToggleInspector();ToggleInspector();results["inspectorCollapses"]=inspectorColumn.Width.Value==0;ToggleInspector();
        var options=Descendants<Button>(this).Single(b=>Equals(b.Tag,"Toggle generation options"));options.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));results["advancedOptionsDisclosure"]=advancedPanel.Visibility==Visibility.Visible;ShowChat();UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".options.png"));options.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        SelectInspector("Sources");results["sourcesInspectorTab"]=inspectorContent.Content==sourcesInspector;SelectInspector("Files");
        NewConversation();results["newChatResetsTaskAndStyle"]=briefEditor.Text==""&&responseStyle.SelectedItem as string=="Build";var emptyCount=conversations.Count;NewConversation();results["untouchedEmptyChatsDoNotAccumulate"]=conversations.Count==emptyCount;UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".welcome.png"));
        var first=conversation;first.Title="Context isolation A";files.Add(new ContextFile(sourcePath,"SOURCE_CONTEXT_A"));webSources.Add(new WebSource("Research A","https://example.org/a","RESEARCH_CONTEXT_A",DateTimeOffset.UtcNow));UpdateContext();
        NewConversation();var second=conversation;second.Title="Context isolation B";files.Add(new ContextFile(Path.Combine(directory,"Second.cs"),"SOURCE_CONTEXT_B"));webSources.Add(new WebSource("Research B","https://example.org/b","RESEARCH_CONTEXT_B",DateTimeOffset.UtcNow));UpdateContext();
        history.SelectedItem=first;var outgoing=string.Join("\n",BuildMessages("check").Select(m=>m.Content));
        results["switchChatRestoresOnlyItsSourceAndResearch"]=outgoing.Contains("SOURCE_CONTEXT_A")&&outgoing.Contains("RESEARCH_CONTEXT_A")&&!outgoing.Contains("SOURCE_CONTEXT_B")&&!outgoing.Contains("RESEARCH_CONTEXT_B")&&workingFiles.DisplayedPaths.Contains(sourcePath);
        history.SelectedItem=second;outgoing=string.Join("\n",BuildMessages("check").Select(m=>m.Content));
        results["switchBackRetainsSecondChatContext"]=outgoing.Contains("SOURCE_CONTEXT_B")&&outgoing.Contains("RESEARCH_CONTEXT_B")&&!outgoing.Contains("SOURCE_CONTEXT_A")&&!outgoing.Contains("RESEARCH_CONTEXT_A");
        idleControls.OfType<Button>().Single(b=>Equals(b.Tag,"Clear context")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));idleControls.OfType<Button>().Single(b=>Equals(b.Tag,"Clear web sources")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));history.SelectedItem=first;var firstPreserved=files.Count==1&&webSources.Count==1;history.SelectedItem=second;results["clearedContextDoesNotReappearOrClearOtherChat"]=firstPreserved&&files.Count==0&&webSources.Count==0;
        NewConversation();results["freshChatCannotInheritPriorContext"]=files.Count==0&&webSources.Count==0;
        var attachmentOnly=conversation;files.Add(new ContextFile(sourcePath,"ATTACHMENT_ONLY"));UpdateContext();NewConversation();results["attachmentOnlyChatIsRetained"]=conversations.Contains(attachmentOnly);history.SelectedItem=attachmentOnly;results["attachmentOnlyChatRestoresContext"]=BuildMessages("check").Any(m=>m.Content.Contains("ATTACHMENT_ONLY"));
        var importFolder=Path.Combine(directory,"cancel-import");Directory.CreateDirectory(importFolder);var importText=new string('x',128*1024);for(var i=0;i<256;i++)File.WriteAllText(Path.Combine(importFolder,$"Source{i}.cs"),importText);
        var beforeImport=files.ToArray();pickerTestAction=picker=>{picker.TestNavigate(importFolder);picker.TestChoose();};idleControls.OfType<Button>().Single(b=>Equals(b.Tag,"Add source folder")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));pickerTestAction=null;
        var importWasBusy=busy;cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));while(busy)await Task.Delay(10);results["cancelFolderImportPreservesExistingContext"]=importWasBusy&&files.SequenceEqual(beforeImport)&&status.Text.Contains("cancelled",StringComparison.OrdinalIgnoreCase);
        ToggleNavigation();HandleWorkspaceShortcut(System.Windows.Input.Key.K,System.Windows.Input.ModifierKeys.Control);results["searchCommandRevealsCollapsedChats"]=navigationColumn.Width.Value>0&&historySearch.IsVisible;if(navigationColumn.Width.Value==0)ToggleNavigation();
        ShowCode();HandleWorkspaceShortcut(System.Windows.Input.Key.L,System.Windows.Input.ModifierKeys.Control);results["composerCommandReturnsFromCodeToChat"]=chatView.Visibility==Visibility.Visible&&codeView.Visibility==Visibility.Collapsed;
        var metadataFolder=Path.Combine(directory,"metadata-guard");Directory.CreateDirectory(metadataFolder);var metadataPath=Path.Combine(metadataFolder,".git");const string metadataOriginal="gitdir: ../synthetic-metadata";File.WriteAllText(metadataPath,metadataOriginal);
        var metadataReview=new CodeSaveWindow("replacement","text"){Owner=this};metadataReview.Loaded+=(_,_)=>{metadataReview.TestPreview(metadataFolder,".git");metadataReview.TestSave();results["gitMetadataSaveRefusedWithVisibleReason"]=metadataReview.Receipt==null&&File.ReadAllText(metadataPath)==metadataOriginal&&Descendants<TextBlock>(metadataReview).Any(t=>t.Text.Contains("Git metadata"));metadataReview.Close();};metadataReview.ShowDialog();
        NewConversation();Width=1080;Height=700;UpdateLayout();
        results["primaryControlsFitNarrowWorkspace"]=new FrameworkElement[]{model,send,voiceMic,tools,add}.All(control=>{var point=control.TranslatePoint(new Point(),this);return control.IsVisible&&point.X>=0&&point.X+control.ActualWidth<=ActualWidth&&point.Y>=0&&point.Y+control.ActualHeight<=ActualHeight;});
        Capture(this,Path.ChangeExtension(smokeOutput!,".narrow.png"));
        await ExerciseProjectNavigation(results);
        await ExerciseGuidanceAndGoals(results);
        await ExerciseCapabilities(results);
        await ExerciseRightPane(results);
        await ExerciseDeletion(results);
        await ExerciseActionQuestions(results);await ExerciseDeepSeek(results);await ExerciseFileAttachments(results);
    }
}
