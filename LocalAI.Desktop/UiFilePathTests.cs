using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal static class UiFilePathTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"file-path-fixture");Directory.CreateDirectory(root);
        FileWorkspaceWindow? window=null;Exception? reviewFailure=null;
        void Check(string key,bool value){rows[key]=value;if(!value)throw new IOException("File UI assertion failed: "+key);}
        async Task Wait(Func<bool> predicate){for(var n=0;n<300;n++){if(predicate())return;await Task.Delay(20);}throw new IOException("File UI fixture timed out.");}
        try
        {
            var file=Path.Combine(root,"Example.cs");File.WriteAllText(file,"class Before {}");var nested=Path.Combine(root,"Child Ω");Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(root,"appsettings.json"),"{}");var roots=new List<string>();var included=new List<ContextFile>();var receipts=new List<FileActionReceipt>();var changes=0;
            window=new(roots,()=>changes++,included.Add,r=>{receipts.Add(r);return Task.CompletedTask;});window.Show();
            Button Button(string label)=>UiDialogPathTests.Find<Button>(window).Single(b=>Equals(b.Content,label));
            TextBox Field(string name)=>UiDialogPathTests.Find<TextBox>(window).Single(b=>AutomationProperties.GetName(b)==name);
            var target=Field("File action path");var destination=Field("File action destination");var address=Field("Workspace folder path");var editor=Field("Workspace file draft");var entries=UiDialogPathTests.Find<ListBox>(window).Single();
            void ChooseFolder(string name,string? path)
            {
                var prior=FolderPicker.TestSelection;
                try{FolderPicker.TestSelection=_=>path;UiPathTests.Click(UiDialogPathTests.Find<Button>(window).Single(b=>AutomationProperties.GetName(b)==name));}
                finally{FolderPicker.TestSelection=prior;}
            }
            ChooseFolder("Choose connection folder",root);Check("fileBrowseButtonReturnsExactSelectedFolder",Field("Connect local or network folder").Text==root&&roots.Count==0);
            async Task Click(string label){UiPathTests.Click(Button(label));await Wait(()=>Button(label).IsEnabled);if(reviewFailure!=null)throw reviewFailure;}
            Field("Connect local or network folder").Text=Path.Combine(root,"missing");await Click("Connect folder");Check("missingFolderDoesNotConnect",roots.Count==0&&changes==0);
            Field("Connect local or network folder").Text=root;await Click("Connect folder");Check("connectFolderButtonListsActualFiles",roots.SequenceEqual(new[]{root})&&changes==1&&entries.Items.Count>0);
            await Click("Connect folder");Check("connectDuplicateDoesNotDuplicateRoot",roots.Count==1&&changes==1);
            Check("connectDoesNotAutomaticallyIncludeFiles",included.Count==0);
            entries.SelectedIndex=0;var selectedBeforeCancel=entries.SelectedItem;ChooseFolder("Choose workspace folder",null);await Task.Delay(50);
            Check("cancelWorkspaceFolderPickerKeepsSelection",ReferenceEquals(entries.SelectedItem,selectedBeforeCancel));
            ChooseFolder("Choose workspace folder",nested);await Wait(()=>UiDialogPathTests.Find<Button>(window).Single(b=>AutomationProperties.GetName(b)=="Choose workspace folder").IsEnabled);
            Check("workspaceFolderPickerNavigatesSelectedConnectedFolder",address.Text==nested&&entries.Items.Count==0);
            await Click("Up");

            address.Text=nested;await Click("Go");Check("goButtonNavigatesUnicodeFolder",address.Text==nested&&entries.Items.Count==0);
            await Click("Up");Check("upButtonReturnsToConnectedRoot",address.Text==root&&entries.Items.Count>0);
            address.Text=Path.GetDirectoryName(root)!;await Click("Go");Check("goOutsideConnectedRootDoesNotChangeLocation",entries.Items.Count>0&&roots.SequenceEqual(new[]{root}));address.Text=root;
            Field("Search file names").Text="Example";await Click("Search names");Check("searchNamesButtonFiltersActualEntries",entries.Items.Count==1&&entries.Items.OfType<ListBoxItem>().Single().Tag is WorkspaceEntry e&&e.Path==file);
            entries.SelectedIndex=0;Check("selectingEntryUpdatesActionPath",target.Text==file);
            Check("fileEntryAccessibleNameIdentifiesExactPath",AutomationProperties.GetName((ListBoxItem)entries.SelectedItem)=="File: "+file);
            await Click("Read / preview");Check("previewButtonLoadsActualFileWithoutIncluding",editor.Text=="class Before {}"&&included.Count==0);
            await Click("Include in chat");Check("includeFileButtonReturnsScreenedActualContent",included.Count==1&&included[0].Path==file&&included[0].Content=="class Before {}");
            target.Text=Path.Combine(root,"appsettings.json");await Click("Include in chat");Check("includeConfigurationIsRejected",included.Count==1);
            await Click("New file draft");Check("newFileDraftButtonDoesNotWrite",target.Text==Path.Combine(root,"NewFile.txt")&&editor.Text.Length==0&&!File.Exists(target.Text));
            window.ReviewTestAction=dialog=>dialog.Close();target.Text=file;editor.Text="class Cancelled {}";await Click("Review write");Check("cancelWriteReviewPreservesFile",File.ReadAllText(file)=="class Before {}"&&receipts.Count==0);
            window.ReviewTestAction=async dialog=>
            {
                try
                {
                    var preview=UiDialogPathTests.Find<Button>(dialog).Single(b=>Equals(b.Content,"Preview exact action"));var apply=UiDialogPathTests.Find<Button>(dialog).Single(b=>Equals(b.Content,"Confirm reviewed action"));
                    Check("fileApplyInitiallyDisabled",!apply.IsEnabled);UiPathTests.Click(preview);await Wait(()=>preview.IsEnabled);
                    Check("filePreviewButtonEnablesExactConfirmation",apply.IsEnabled);UiPathTests.Click(apply);
                }
                catch(Exception error){reviewFailure=error;if(dialog.IsVisible)dialog.Close();}
            };
            editor.Text="class Reviewed {}";await Click("Review write");Check("reviewWriteButtonsSaveActualBytesWithBackup",File.ReadAllText(file)=="class Reviewed {}"&&receipts.Last().RecoveryPath is {} backup&&File.ReadAllText(backup)=="class Before {}");
            var created=Path.Combine(root,"Created folder");target.Text=created;await Click("New folder");Check("newFolderButtonsCreateExactDirectory",Directory.Exists(created));
            var copied=Path.Combine(root,"Copied.cs");target.Text=file;destination.Text=copied;await Click("Copy");Check("copyButtonsPreserveSourceAndCreateDestination",File.ReadAllText(copied)==File.ReadAllText(file));
            var moved=Path.Combine(root,"Moved.cs");target.Text=copied;destination.Text=moved;await Click("Move / rename");Check("moveButtonsChangeExactPathWithRecovery",!File.Exists(copied)&&File.Exists(moved)&&receipts.Last().RecoveryPath is {} moveBackup&&File.Exists(moveBackup));
            target.Text=moved;await Click("Delete with recovery");var deleted=receipts.Last();Check("deleteButtonsRetainRecoverableBytes",!File.Exists(moved)&&deleted.RecoveryPath is {} recovery&&File.ReadAllText(recovery)=="class Reviewed {}");
            await Click("Recovery files");Check("recoveryButtonListsSavedRecoveryItems",entries.Items.Count>0&&entries.Items.OfType<ListBoxItem>().Any(i=>i.Tag is WorkspaceEntry r&&r.Path==deleted.RecoveryPath));
            var restored=Path.Combine(root,"Restored.cs");target.Text=deleted.RecoveryPath!;destination.Text=restored;await Click("Restore");Check("restoreButtonsRecoverToExactNewPath",File.ReadAllText(restored)=="class Reviewed {}");
            foreach(var label in new[]{"Source control & Teams","Run commands"})
            {
                Window? opened=null;_=window.Dispatcher.BeginInvoke(()=>{opened=Application.Current.Windows.OfType<Window>().SingleOrDefault(w=>w.Owner==window&&w.IsVisible);opened?.Close();});
                await Click(label);Check("fileToolbarDialogRoute_"+label,opened!=null&&!opened.IsVisible);
            }
            window.UpdateLayout();QaCapture.Save(window,Path.ChangeExtension(output,".png"));
            await Click("Disconnect");Check("disconnectButtonRemovesAccessAndEntries",roots.Count==0&&entries.Items.Count==0&&changes==2);
            target.Text=file;await Click("Read / preview");Check("disconnectedFileCannotBeRead",UiDialogPathTests.Find<TextBlock>(window).Any(t=>t.Text.Contains("connected",StringComparison.OrdinalIgnoreCase)));
            window.Close();window=null;
            var groupFirst=Path.Combine(root,"Group first");var groupSecond=Path.Combine(root,"Group second");
            var group=new FolderBatchReviewWindow(new FileWorkspace([root]),[new("mkdir",groupFirst),new("mkdir",groupSecond)]);var groupDone=new TaskCompletionSource();
            group.Loaded+=async(_,_)=>
            {
                try
                {
                    var preview=UiDialogPathTests.Find<Button>(group).Single(b=>Equals(b.Content,"Preview all folders"));var apply=UiDialogPathTests.Find<Button>(group).Single(b=>Equals(b.Content,"Create reviewed folders"));
                    Check("folderGroupApplyInitiallyDisabled",!apply.IsEnabled);UiPathTests.Click(preview);await Wait(()=>preview.IsEnabled);
                    Check("folderGroupPreviewButtonHasNoFileEffects",apply.IsEnabled&&!Directory.Exists(groupFirst)&&!Directory.Exists(groupSecond));
                    UiPathTests.Click(apply);await Wait(()=>!group.IsVisible);Check("folderGroupApplyButtonCreatesBothExactDirectories",group.Receipts.Count==2&&Directory.Exists(groupFirst)&&Directory.Exists(groupSecond));
                }
                catch(Exception error){reviewFailure=error;if(group.IsVisible)group.Close();}
                finally{groupDone.TrySetResult();}
            };group.ShowDialog();await groupDone.Task.WaitAsync(TimeSpan.FromSeconds(8));if(reviewFailure!=null)throw reviewFailure;
            var individual=new FolderBatchReviewWindow(new FileWorkspace([root]),[new("mkdir",Path.Combine(root,"Individual first")),new("mkdir",Path.Combine(root,"Individual second"))]);
            individual.Loaded+=(_,_)=>UiPathTests.Click(UiDialogPathTests.Find<Button>(individual).Single(b=>Equals(b.Content,"Review one at a time")));individual.ShowDialog();
            Check("folderGroupIndividualButtonReturnsWithoutCreatingDirectories",individual.ReviewIndividually&&!Directory.Exists(Path.Combine(root,"Individual first")));
        }
        catch(Exception error){rows["error"]=error.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==35&&rows.Values.All(v=>v is true)?0:1);
    }
}
