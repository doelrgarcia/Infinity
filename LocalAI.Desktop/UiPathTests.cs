using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace LocalAI.Desktop;

// Runs inside the real WPF application against an isolated --ui-session.
// Clicking a handler is recorded separately from checking its resulting state.
internal static class UiPathTests
{
    internal static void Click(ButtonBase button)
    {
        if(!button.IsEnabled)throw new IOException("Disabled control: "+Label(button));
        button.GetType().GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);
    }
    internal static string Label(ButtonBase button)=>AutomationProperties.GetName(button) is {Length:>0} name?name:button.Content as string??button.Tag as string??"";
    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var rows=new Dictionary<string,object>();MainWindow? window=null;
        try
        {
            var session=Path.Combine(Path.GetDirectoryName(output)!,"path-session","session.json");
            window=new MainWindow(["--ui-session",session]);window.Show();await window.ExerciseUiPaths(rows,Path.GetDirectoryName(output)!);
            window.UpdateLayout();QaCapture.Save(window,Path.ChangeExtension(output,".png"));
        }
        catch(Exception error){rows["error"]=error.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Application.Current.Shutdown(rows.Count==150&&rows.Values.All(v=>v is true)?0:1);
    }
}

public sealed partial class MainWindow
{
    internal async Task ExerciseUiPaths(Dictionary<string,object> rows,string output)
    {
        void Check(string name,bool value){rows[name]=value;if(!value)throw new IOException("UI path assertion failed: "+name);}
        Button Find(string name)=>Descendants<Button>(this).First(b=>UiPathTests.Label(b)==name||Equals(b.Content,name)||Equals(b.Tag,name));
        void Click(string name){UpdateLayout();UiPathTests.Click(Find(name));UpdateLayout();}
        Button MenuButton(string name)=>Descendants<Button>(this).Single(b=>AutomationProperties.GetName(b)==name);
        void Menu(string button,string item)
        {
            var owner=MenuButton(button);UiPathTests.Click(owner);var menu=owner.ContextMenu!;
            Check("menuOpens_"+item,menu.IsOpen);
            var entry=menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,item));
            menu.IsOpen=false;entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        async Task Modal(string key,Action trigger,Action<Window> inspect)
        {
            Window? opened=null;Exception? failure=null;var finished=new TaskCompletionSource();
            // A scoped Dispatcher callback runs after ShowDialog has loaded.
            _=Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    for(var n=0;n<300&&opened==null;n++){opened=Application.Current.Windows.OfType<Window>().SingleOrDefault(w=>(w.Owner==this||w is ChatAssistantWindow)&&w.IsVisible);if(opened==null)await Task.Delay(20);}
                    if(opened!=null)inspect(opened);
                }
                catch(Exception error){failure=error;}
                finally{if(opened?.IsVisible==true)opened.Close();finished.TrySetResult();}
            },DispatcherPriority.ContextIdle);
            trigger();await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if(failure!=null)throw new IOException(key,failure);
            Check(key,opened!=null&&!opened.IsVisible);
        }
        void Cancel(Window dialog)
        {
            var cancel=Descendants<Button>(dialog).FirstOrDefault(b=>b.IsCancel);
            if(cancel==null){dialog.Close();return;}
            UiPathTests.Click(cancel);Check("cancelCloses_"+dialog.Title,!dialog.IsVisible);
        }
        var root=Path.Combine(output,"path-fixture");Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"Example.cs"),"public class Example { public int Add(int a,int b)=>a+b; }");
        foreach(var starter in new[]{"Build a feature","Explain some code","Find and fix a bug","Research an approach"})
        {prompt.Clear();Click(starter);Check("starterButtonPopulatesDraft_"+starter,prompt.Text.Length>20&&conversation.Messages.Count==0);}
        prompt.Clear();UiPathTests.Click(send);Check("emptySendButtonDoesNotStartRequest",!busy&&conversation.Messages.Count==0);
        prompt.Text="Retain this draft";var original=conversation.Id;
        Click("Sidebar");Check("sidebarButtonHidesNavigation",navigationColumn.Width.Value==0);
        Click("Sidebar");Check("sidebarButtonRestoresNavigation",navigationColumn.Width.Value>0);
        await Modal("newProjectCancelRoute",()=>Click("New project"),Cancel);
        Check("newProjectCancelPreservesChatAndDraft",conversation.Id==original&&prompt.Text=="Retain this draft");
        projectDialogTest=dialog=>dialog.TestSubmit("UI path project",root);
        Click("New project");projectDialogTest=null;
        var project=VisibleProjects.Single(p=>p.Name=="UI path project");
        Check("newProjectButtonPersistsAndOpensProject",conversation.ProjectId==project.Id&&conversation.WorkspaceRoots.Contains(root));
        var first=conversation.Id;Click("New chat in project: UI path project");var second=conversation.Id;
        Check("nestedNewChatButtonCreatesSecondChat",first!=second&&VisibleChats.Count(c=>c.ProjectId==project.Id)==2);
        Click("Collapse chats in project: UI path project");Check("collapseProjectHidesChildren",!expandedChatProjects.Contains(project.Id));
        Click("Expand chats in project: UI path project");Check("expandProjectShowsChildren",expandedChatProjects.Contains(project.Id));
        var open=Descendants<Button>(projectRows).Single(b=>Equals(b.Tag,first));UiPathTests.Click(open);
        Check("nestedChatButtonSwitchesChat",conversation.Id==first);
        var oldTitle=conversation.Title;
        await Modal("renameChatCancelRoute",()=>{var menu=ChatMenu(conversation);menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"Rename chat")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));},Cancel);
        Check("renameCancelPreservesTitle",conversation.Title==oldTitle);
        await Modal("renameChatConfirmRoute",()=>{var menu=ChatMenu(conversation);menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"Rename chat")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));},dialog=>
        {
            var field=Descendants<TextBox>(dialog).Single(t=>AutomationProperties.GetName(t)=="Chat name");field.Text="Renamed Ω chat";
            UiPathTests.Click(Descendants<Button>(dialog).Single(b=>Equals(b.Content,"Rename")));
        });
        Check("renameButtonUpdatesPersistentChat",conversation.Title=="Renamed Ω chat"&&store.Load(out _).Any(c=>c.Id==first&&c.Title=="Renamed Ω chat"));
        Click("Show all chats");Check("allChatsButtonClearsProjectFilter",chatProjectFilter==null);
        Click("Open project: UI path project");Check("projectButtonRestoresProjectFilter",chatProjectFilter==project.Id);
        await Modal("projectDeleteCancelRoute",()=>
        {
            var actions=Find("Project actions: UI path project");UiPathTests.Click(actions);Check("projectActionsButtonOpensMenu",actions.ContextMenu!.IsOpen);actions.ContextMenu.IsOpen=false;
            actions.ContextMenu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"Delete project")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        },dialog=>
        {
            var includeChats=Descendants<CheckBox>(dialog).Single();UiPathTests.Click(includeChats);Check("deleteProjectCheckboxCanRetainChats",((DeleteItemWindow)dialog).DeleteChats==false);UiPathTests.Click(includeChats);Check("deleteProjectCheckboxCanIncludeChats",((DeleteItemWindow)dialog).DeleteChats);Cancel(dialog);
        });Check("cancelProjectDeleteRetainsProjectAndChats",VisibleProjects.Any(p=>p.Id==project.Id)&&VisibleChats.Count(c=>c.ProjectId==project.Id)==2);
        await Modal("chatDeleteCancelRoute",()=>
        {
            var menu=ChatMenu(conversation);menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"Delete chat")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        },Cancel);Check("cancelChatDeleteRetainsChat",VisibleChats.Any(c=>c.Id==conversation.Id));
        historySearch.Text="no matching synthetic title";Check("searchHidesNonmatchingProject",!Descendants<Button>(projectRows).Any(b=>UiPathTests.Label(b)=="Open project: UI path project"));historySearch.Clear();
        foreach(var section in new[]{"Files","Sources","Task"})
        {Click(section);Check("inspectorTab_"+section,inspectorContent.Content==(section=="Files"?filesInspector:section=="Sources"?sourcesInspector:briefInspector));}
        briefEditor.Text="Synthetic UI brief";Click("Save brief");Check("saveBriefButtonPersistsText",store.Load(out _).Single(c=>c.Id==conversation.Id).TaskBrief=="Synthetic UI brief");
        Click("Close right pane");Check("rightPaneCloseButtonHidesPane",inspectorColumn.Width.Value==0);
        Menu("Open Infinity tools","Files and sources");Check("toolsFilesRouteRestoresPane",inspectorColumn.Width.Value>0&&inspectorContent.Content==filesInspector);
        Menu("Open Infinity tools","Chat options");Check("chatOptionsShowsGenerationControls",advancedPanel.Visibility==Visibility.Visible);
        await Modal("advancedDebugButtonRoute",()=>Click("Run code and diagnose errors with Infinity"),dialog=>{Check("advancedDebugButtonOpensCorrectWindow",dialog is DebugWindow);dialog.Close();});
        Click("Browse websites inside Infinity");for(var n=0;n<300&&appBrowser?.Current?.CoreWebView2?.DocumentTitle!="Infinity Browser";n++)await Task.Delay(20);
        Check("advancedBrowserButtonLoadsWelcomeInRightPane",rightPaneMode=="Browser"&&appBrowser?.Current?.CoreWebView2?.DocumentTitle=="Infinity Browser");
        var browserInstance=appBrowser;Menu("Open Infinity tools","Browser");Check("toolsBrowserMenuReusesCurrentBrowser",ReferenceEquals(appBrowser,browserInstance)&&rightPaneMode=="Browser");
        UiPathTests.Click(Descendants<Button>(this).Single(b=>AutomationProperties.GetName(b)=="Browser"));Check("rightPaneBrowserButtonReusesCurrentBrowser",ReferenceEquals(appBrowser,browserInstance)&&rightPaneMode=="Browser");
        Menu("Open Infinity tools","Chat options");Check("chatOptionsHidesGenerationControls",advancedPanel.Visibility==Visibility.Collapsed);
        var oldVisibility=status.Visibility;Menu("Open Infinity tools","Run details");Check("runDetailsToggleChangesVisibility",status.Visibility!=oldVisibility);
        Menu("Open Infinity tools","Run details");Check("runDetailsToggleRestoresVisibility",status.Visibility==oldVisibility);
        foreach(var (item,title) in new[]{("Deleted items","Deleted items"),("Isolated workspace","Isolated workspace"),("Project instructions","Project instructions"),("Project skills","Project skills"),("Goal","Set a goal"),("AI apps and settings","AI apps & CLI")})
            await Modal("toolsDialog_"+item,()=>Menu("Open Infinity tools",item),dialog=>{Check("dialogTitle_"+item,dialog.Title==title);Cancel(dialog);});
        Menu("Open Infinity tools","Code workspace");Check("codeWorkspaceRouteShowsEditor",codeView.Visibility==Visibility.Visible);
        Click("Back to chat");Check("backToChatButtonShowsComposer",chatView.Visibility==Visibility.Visible);
        Menu("Open Infinity tools","Tasks and approvals");Check("tasksMenuOpensTaskSurface",AgentSurfaceActive);
        Click("Back to chat");Check("taskBackToChatRestoresComposer",!AgentSurfaceActive&&chatView.Visibility==Visibility.Visible);
        foreach(var (item,section) in new[]{("Review attached files","Files"),("Review web sources","Sources")})
        {Menu("Add files or web sources",item);Check("addMenuReviewRoute_"+item,inspectorContent.Content==(section=="Files"?filesInspector:sourcesInspector));}
        foreach(var item in new[]{"Add files","Add folder","Open document"})
            await Modal("addMenuPickerCancel_"+item,()=>Menu("Add files or web sources",item),dialog=>{Check("pickerTitle_"+item,dialog is SourcePicker);Cancel(dialog);});
        await Modal("addMenuSkillCancel",()=>Menu("Add files or web sources","Project skill"),Cancel);
        await Modal("chatActivityMenuRoute",()=>Menu("Open Infinity tools","Chat activity"),dialog=>{Check("chatActivityMenuOpensCorrectWindow",dialog is ChatAssistantWindow);dialog.Close();});
        Click("Files");
        foreach(var (button,type) in new[]{("Files on computer and network",typeof(FileWorkspaceWindow)),("Source control and Teams",typeof(SourceControlWindow)),("Review and run commands",typeof(CommandWindow))})
            await Modal("fileToolbarRoute_"+button,()=>Click(button),dialog=>{Check("fileToolbarWindow_"+button,dialog.GetType()==type);dialog.Close();});
        await Modal("debugMenuRoute",()=>Menu("Open Infinity tools","Debug code"),dialog=>{Check("debugMenuOpensCorrectWindow",dialog is DebugWindow);dialog.Close();});
        await Modal("researchEmptySelectionRoute",()=>Menu("Add files or web sources","Search web or add URL"),dialog=>
        {
            UiPathTests.Click(Descendants<Button>(dialog).Single(b=>Equals(b.Content,"Include selected source excerpts")));
            Check("researchEmptySelectionShowsMessage",Descendants<TextBlock>(dialog).Any(t=>t.Text=="Select one or more search results first."));
            Check("researchEmptySelectionRetainsDialog",dialog.IsVisible);
        });
        Click("Files");var before=files.Count;
        pickerTestAction=p=>{p.TestSetAddress(root);p.TestNavigate(root);p.TestChoose("Example.cs");};
        Click("Add source files");for(var n=0;n<100&&files.Count==before;n++)await Task.Delay(20);pickerTestAction=null;Check("filePickerResultImportsActualContent",files.Count>before&&files.Any(f=>f.Content.Contains("public class Example")));
        var fileButton=Descendants<Button>(workingFiles).Single(b=>AutomationProperties.GetName(b)=="Working file: "+Path.Combine(root,"Example.cs"));UiPathTests.Click(fileButton);
        Check("workingFileButtonShowsActualSource",codeEditor.Text.Contains("public class Example")||documentText.Text.Contains("public class Example"));
        ShowChat();Click("Workspace");Click("Files");var folderButton=Descendants<Button>(workingFiles).Single(b=>AutomationProperties.GetName(b).StartsWith("Folder: "));UiPathTests.Click(folderButton);Check("workingFolderButtonCollapsesMembers",!fileButton.IsVisible);UiPathTests.Click(folderButton);Check("workingFolderButtonExpandsMembers",fileButton.IsVisible);
        var removedPath=Path.Combine(root,"Example.cs");selectedSourcePath=removedPath;Click("Remove selected file");Check("removeSelectedFileButtonRemovesContext",!files.Any(f=>f.Path==removedPath));
        files.Add(new(Path.Combine(root,"Example.cs"),"SYNTHETIC"));UpdateContext();Click("Clear context");Check("clearContextButtonRemovesAllFiles",files.Count==0);
        Click("Sources");webSources.Add(new("Synthetic","https://example.invalid/","Fixture excerpt",DateTimeOffset.UtcNow));UpdateContext();
        await Modal("reviewSourcesButtonRoute",()=>Click("Review web sources"),dialog=>Check("sourceReviewContainsIncludedExcerpt",Descendants<TextBox>(dialog).Single().Text.Contains("Fixture excerpt")));
        await Modal("researchInspectorButtonRoute",()=>Click("Web research…"),dialog=>{Check("researchInspectorOpensCorrectWindow",dialog is ResearchWindow);dialog.Close();});
        Click("Clear web sources");Check("clearSourcesButtonRemovesAllSources",webSources.Count==0);
        var attachment=Path.Combine(root,"Brief.txt");File.WriteAllText(attachment,"SYNTHETIC_ATTACHMENT_PREVIEW");
        try
        {
            AttachmentPathsTest=()=>null;Menu("Add files or web sources","Attach files and images");Check("chatAttachmentPickerCancelRetainsDraft",DraftFiles.Count==0);
            AttachmentPathsTest=()=>[attachment];Menu("Add files or web sources","Attach files and images");Check("chatAttachButtonUsesChosenFile",DraftFiles.Count==1);
            AttachmentPathsTest=()=>[Path.Combine(root,"missing.txt")];Menu("Add files or web sources","Attach files and images");Check("chatAttachmentFailureRetainsExistingDraft",DraftFiles.Count==1&&status.Text.Contains("Could not attach files"));
        }
        finally{AttachmentPathsTest=null;}
        UiPathTests.Click(Descendants<Button>(imageTray).Single(b=>b.Content is string text&&text.StartsWith("Brief.txt")));
        Check("draftAttachmentPreviewButtonShowsExtractedText",documentText.Text.Contains("SYNTHETIC_ATTACHMENT_PREVIEW")&&rightPaneMode=="Document");
        Click("Workspace");Check("workspaceRightPaneButtonRestoresWorkspace",rightPaneBody.Content==workspaceInspector);Click("Document");Check("documentRightPaneButtonRestoresPreview",rightPaneBody.Content==documentPane);
        UiPathTests.Click(Descendants<Button>(imageTray).Single(b=>Equals(b.Content,"Remove")));Check("attachmentRemoveButtonClearsUnsentDraft",DraftFiles.Count==0);
        try
        {
            clipboardImageTest=()=>null;Menu("Add files or web sources","Paste screenshot");Check("pasteScreenshotWithoutImageShowsActionableMessage",DraftImages.Count==0&&status.Text.Contains("does not contain an image"));
            var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(2,2,96,96,System.Windows.Media.PixelFormats.Bgra32,null,Enumerable.Repeat((byte)128,16).ToArray(),8);
            clipboardImageTest=()=>bitmap;Menu("Add files or web sources","Paste screenshot");Check("pasteScreenshotButtonAddsSyntheticImagePreview",DraftImages.Count==1&&DraftImages[0].Name=="Clipboard.png"&&ImagePreview(DraftImages[0]).PixelWidth==2);
            Menu("Add files or web sources","Paste screenshot");Check("pasteScreenshotDoesNotDuplicateSameImage",DraftImages.Count==1);
            UiPathTests.Click(Descendants<Button>(imageTray).Single(b=>Equals(b.Content,"Remove")));Check("removeImageButtonClearsUnsentImage",DraftImages.Count==0);
            clipboardImageTest=()=>throw new IOException("Synthetic clipboard busy");Menu("Add files or web sources","Paste screenshot");Check("clipboardFailureShowsErrorWithoutChangingDraft",DraftImages.Count==0&&status.Text.Contains("Synthetic clipboard busy"));
        }
        finally{clipboardImageTest=null;}
        ShowChat();active=new CancellationTokenSource();SetBusy(true);
        try
        {
            prompt.Text="Synthetic queued follow-up";chatFollowMode.SelectedItem="Queue";UiPathTests.Click(send);
            Check("busySendButtonQueuesWithoutDispatch",conversation.FollowUps.Last() is {Text:"Synthetic queued follow-up",Mode:"Queue",Status:"Pending"}&&prompt.Text.Length==0);
            prompt.Text="Synthetic steering follow-up";chatFollowMode.SelectedItem="Steer";UiPathTests.Click(send);
            Check("busySendButtonPersistsSteeringMode",store.Load(out _).Single(c=>c.Id==conversation.Id).FollowUps.Last().Mode=="Steer");
            UiPathTests.Click(cancel);Check("stopReplyButtonCancelsActiveRequest",active.IsCancellationRequested);
        }
        finally{active.Dispose();active=null;SetBusy(false);}
        prompt.Text="Preserve this unfinished draft";Menu("Open Infinity tools","Continue saved follow-ups");await Task.Delay(30);
        Check("continueSavedFollowUpsPreservesUnfinishedDraft",prompt.Text=="Preserve this unfinished draft"&&conversation.FollowUps.All(f=>f.Status=="Pending")&&status.Text.Contains("waiting"));
        conversation.FollowUps.Clear();store.Save(conversation);prompt.Text="Rename the chat";
        await Modal("strongerHelpCancelRoute",()=>Menu("Open Infinity tools","Ask a stronger AI"),dialog=>{Check("strongerHelpShowsReviewBeforeSending",dialog is CliHandoffWindow);Cancel(dialog);});
        Check("cancelStrongerHelpPreservesDraft",prompt.Text=="Rename the chat");prompt.Clear();
        var chatCount=conversations.Count;prompt.Text="new chat called Send button fixture";UiPathTests.Click(send);await Task.Delay(30);
        Check("sendButtonDispatchesLocalAppCommand",conversations.Count==chatCount+1&&conversation.Title=="Send button fixture"&&!busy);
        Click("New conversation");Check("newChatButtonCreatesAnotherProjectChat",conversations.Count==chatCount+2&&conversation.ProjectId==project.Id);
        conversation.Messages.Add(new("user","File tool results: SYNTHETIC_UI_DETAILS_ONLY"));conversation.Messages.Add(new("assistant","Synthetic result preview."));Render();
        var workDetails=Descendants<Expander>(transcript).Single(e=>Equals(e.Tag,"FileActivityDetails"));workDetails.ApplyTemplate();UpdateLayout();Check("workDetailsInitiallyCollapsed",!workDetails.IsExpanded);
        var detailsToggle=UiDialogPathTests.Find<System.Windows.Controls.Primitives.ToggleButton>(workDetails).Single();UiPathTests.Click(detailsToggle);Check("workDetailsToggleShowsRetainedReceiptText",workDetails.IsExpanded&&((TextBox)workDetails.Content).Text.Contains("SYNTHETIC_UI_DETAILS_ONLY"));UiPathTests.Click(detailsToggle);Check("workDetailsToggleHidesDetailsAgain",!workDetails.IsExpanded);
        ShowChat();
    }
}
