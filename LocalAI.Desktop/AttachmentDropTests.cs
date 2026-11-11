using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal static class AttachmentDropTests
{
    internal static DragEventArgs Raise(UIElement target,IDataObject data,RoutedEvent route,DragDropEffects effects=DragDropEffects.Copy)
    {
        var args=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[data,DragDropKeyStates.None,effects,target,new Point(1,1)],null)!;
        args.RoutedEvent=route;target.RaiseEvent(args);return args;
    }
    internal static DragEventArgs Files(UIElement target,RoutedEvent route,params string[] paths)
    {
        var data=new DataObject();data.SetData(DataFormats.FileDrop,paths,false);return Raise(target,data,route);
    }
    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var rows=new Dictionary<string,object>();MainWindow? window=null;
        try{window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"drop-session","session.json")]);window.Show();await window.ExerciseAttachmentDrops(rows,Path.GetDirectoryName(output)!);if(window.IsVisible)QaCapture.Save(window,Path.ChangeExtension(output,".png"));}
        catch(Exception error){rows["error"]=error.ToString();}finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==42&&rows.Values.All(v=>v is true)?0:1);
    }
}

public sealed partial class MainWindow
{
    internal async Task ExerciseAttachmentDrops(Dictionary<string,object> rows,string output)
    {
        void Check(string key,bool value){rows[key]=value;if(!value)throw new IOException("Attachment drop assertion failed: "+key);}
        var root=Path.Combine(output,"drop-fixture Ω");Directory.CreateDirectory(root);
        var text=Path.Combine(root,"Brief Ω.txt");File.WriteAllText(text,"SYNTHETIC_DROP_TEXT");var second=Path.Combine(root,"Second.cs");File.WriteAllText(second,"class DropFixture {}");
        var doc=Path.Combine(root,"Notes.docx");using(var zip=ZipFile.Open(doc,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>SYNTHETIC_DROPPED_DOCX</w:t></w:r></w:p></w:body></w:document>");}
        string Image(int color){var path=Path.Combine(root,"Image "+color+".png");var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)color,16).ToArray(),8);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);return path;}
        var image=Image(20);var bad=Path.Combine(root,"Unsupported.exe");File.WriteAllText(bad,"not executable");var secret=Path.Combine(root,"appsettings.json");File.WriteAllText(secret,"{}");var binary=Path.Combine(root,"Binary.cs");File.WriteAllBytes(binary,[0,1,2,0]);
        var beforeMessages=conversation.Messages.Count;var beforeRoots=conversation.WorkspaceRoots.ToArray();prompt.Text="Keep my chat draft";
        Check("chatSurfaceAdvertisesFileDrops",chatView.AllowDrop&&prompt.ToolTip.ToString()!.Contains("Drop files"));
        var hover=AttachmentDropTests.Files(prompt,DragDrop.PreviewDragOverEvent,text);Check("editorFileHoverBubblesToChatAsCopy",hover.Handled&&hover.Effects==DragDropEffects.Copy);
        hover=AttachmentDropTests.Files(prompt,DragDrop.PreviewDragEnterEvent,Path.Combine(root,"missing.txt"));Check("dragEnterDoesNotReadOrAttachFiles",hover.Effects==DragDropEffects.Copy&&DraftFiles.Count==0&&DraftImages.Count==0);
        var move=new DataObject();move.SetData(DataFormats.FileDrop,new[]{text},false);hover=AttachmentDropTests.Raise(prompt,move,DragDrop.PreviewDragOverEvent,DragDropEffects.Move);Check("moveOnlyDragIsNotAdvertisedAsFileMove",hover.Handled&&hover.Effects==DragDropEffects.None);
        var drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,text);Check("editorDropAttachesExactExtractedText",drop.Handled&&drop.Effects==DragDropEffects.Copy&&DraftFiles.Single().Text=="SYNTHETIC_DROP_TEXT");
        Check("filePathsNeverReplaceTypedChatPrompt",prompt.Text=="Keep my chat draft");
        AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,text);Check("repeatedFileDropDoesNotDuplicateAttachment",DraftFiles.Count==1);
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,second,bad);Check("unsupportedBatchHasNoPartialAttachment",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1&&status.Text.Contains("Could not attach dropped files"));
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,secret);Check("privateConfigurationDropIsRejected",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1&&status.Text.Contains("excluded"));
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,binary);Check("binarySourceDropIsRejected",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1);
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,Path.Combine(root,"missing.txt"));Check("missingFileDropReportsErrorAndRetainsDraft",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1&&prompt.Text=="Keep my chat draft");
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,root);Check("folderDropDoesNotChangeAccessScope",drop.Effects==DragDropEffects.None&&conversation.WorkspaceRoots.SequenceEqual(beforeRoots)&&status.Text.Contains("connect a folder"));
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,Enumerable.Repeat(text,9).ToArray());Check("oversizedDropBatchIsBoundedBeforeReading",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1&&status.Text.Contains("four documents"));
        var many=Enumerable.Range(1,5).Select(n=>{var p=Path.Combine(root,"Document "+n+".txt");File.WriteAllText(p,"Fixture "+n);return p;}).ToArray();drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,many);Check("documentCountLimitIsAtomicForDrops",drop.Effects==DragDropEffects.None&&DraftFiles.Count==1);
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,Enumerable.Range(1,5).Select(n=>Image(30+n)).ToArray());Check("imageCountLimitIsAtomicForDrops",drop.Effects==DragDropEffects.None&&DraftImages.Count==0&&DraftFiles.Count==1);
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent);Check("emptyFileDropReportsActionableError",drop.Effects==DragDropEffects.None&&status.Text.Contains("readable files"));
        drop=AttachmentDropTests.Raise(prompt,move,DragDrop.PreviewDropEvent,DragDropEffects.Move);Check("moveOnlyDropPreservesOriginalFile",drop.Effects==DragDropEffects.None&&File.ReadAllText(text)=="SYNTHETIC_DROP_TEXT");
        var ordinary=new DataObject(DataFormats.UnicodeText,"ordinary text");AttachmentDropTests.Raise(chatView,ordinary,DragDrop.PreviewDropEvent);Check("plainTextDropDoesNotBecomeFileAttachment",DraftFiles.Count==1&&DraftImages.Count==0&&prompt.Text=="Keep my chat draft");
        drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,doc,image);Check("mixedDocumentAndImageDropCreatesBothPreviews",drop.Effects==DragDropEffects.Copy&&DraftFiles.Count==2&&DraftFiles.Any(f=>f.Name=="Notes.docx"&&f.Text.Contains("SYNTHETIC_DROPPED_DOCX"))&&DraftImages.Count==1&&ImagePreview(DraftImages[0]).PixelWidth==2);
        AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,image);Check("repeatedImageDropDoesNotDuplicatePreview",DraftImages.Count==1);
        UiPathTests.Click(UiDialogPathTests.Find<Button>(imageTray).Single(b=>Equals(b.Content,"Notes.docx · extracted text")));Check("droppedDocxPreviewButtonShowsActualExtractedText",documentText.Text.Contains("SYNTHETIC_DROPPED_DOCX"));ShowChat();
        Check("chatDropNeverSendsOrGrantsFolderAccess",conversation.Messages.Count==beforeMessages&&conversation.WorkspaceRoots.SequenceEqual(beforeRoots));
        SetBusy(true);try{drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,second);Check("busyChatDropRetainsExistingAttachments",drop.Effects==DragDropEffects.None&&DraftFiles.Count==2&&status.Text.Contains("Wait or stop"));}finally{SetBusy(false);}
        var fake=new TestDictation();voiceSessionFactory=()=>fake;try{ToggleDictation();drop=AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,second);Check("dictatingChatRejectsAttachmentDrops",drop.Effects==DragDropEffects.None&&DraftFiles.Count==2&&dictation!=null);}finally{CancelDictation();voiceSessionFactory=null;}
        var firstChat=conversation;NewConversation();AttachmentDropTests.Files(prompt,DragDrop.PreviewDropEvent,second);Check("dropTargetsCurrentChatAndRetainsPriorChatDraft",DraftFiles.Count==1&&DraftFiles[0].Name=="Second.cs"&&fileDrafts[firstChat.Id].Count==2&&imageDrafts[firstChat.Id].Count==1);
        OpenAgent();Agent.Paused=true;Agent.WatchesPaused=true;var view=agentView!;var composer=UiDialogPathTests.Find<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Message Infinity Agent");composer.Text="Keep my Agent draft";var agentMessages=Agent.Messages.Count;
        Check("agentSurfaceAdvertisesFileDrops",view.AllowDrop&&composer.ToolTip.ToString()==AttachmentDropTarget.Hint);
        drop=AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,text);Check("agentEditorDropCreatesAttachmentPreview",drop.Handled&&drop.Effects==DragDropEffects.Copy&&view.TestAttachmentCounts.Files==1);
        Check("agentDropKeepsPromptAndNeverSends",view.TestComposer=="Keep my Agent draft"&&Agent.Messages.Count==agentMessages);
        AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,text);Check("agentDropDeduplicatesFiles",view.TestAttachmentCounts.Files==1);
        drop=AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,second,bad);Check("agentInvalidBatchPreservesExistingDraft",drop.Effects==DragDropEffects.None&&view.TestAttachmentCounts.Files==1&&UiDialogPathTests.Find<TextBlock>(view).Any(t=>t.Text.Contains("Could not attach dropped files")));
        drop=AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,doc,image);Check("agentMixedDropCreatesDocumentAndImagePreviews",drop.Effects==DragDropEffects.Copy&&view.TestAttachmentCounts==(2,1));
        UiPathTests.Click(UiDialogPathTests.Find<Button>(view).Single(b=>Equals(b.Content,"Notes.docx · text")));Check("agentDroppedDocxPreviewShowsActualText",documentText.Text.Contains("SYNTHETIC_DROPPED_DOCX"));OpenAgent();
        fake=new TestDictation();view.VoiceSessionFactoryTest=()=>fake;try{UiPathTests.Click(UiDialogPathTests.Find<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Dictate Agent prompt"));drop=AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,second);Check("dictatingAgentRejectsDropsWithoutLosingDraft",drop.Effects==DragDropEffects.None&&view.TestAttachmentCounts==(2,1)&&view.TestComposer=="Keep my Agent draft");}finally{view.StopVoice();view.VoiceSessionFactoryTest=null;}
        agentRequest=new CancellationTokenSource();try{drop=AttachmentDropTests.Files(composer,DragDrop.PreviewDropEvent,second);Check("workingAgentRejectsAttachmentDrops",drop.Effects==DragDropEffects.None&&view.TestAttachmentCounts==(2,1));}finally{agentRequest.Dispose();agentRequest=null;}
        Check("dropsPreserveOriginalSourceFiles",File.ReadAllText(text)=="SYNTHETIC_DROP_TEXT"&&File.Exists(doc)&&File.Exists(image)&&File.ReadAllText(second)=="class DropFixture {}");
        Check("agentDropDoesNotChangeFolderAccess",Agent.Roots.Count==0&&conversation.WorkspaceRoots.Count==0);
        ShowChat();model.ApplyTemplate();UpdateLayout();var arrow=UiDialogPathTests.Find<System.Windows.Controls.Primitives.ToggleButton>(model).Single();UiPathTests.Click(arrow);
        Check("sharedDropdownArrowOpensActualModelPopup",model.IsDropDownOpen);UiPathTests.Click(arrow);Check("sharedDropdownArrowClosesActualModelPopup",!model.IsDropDownOpen);
        Button Chrome(string name)=>UiDialogPathTests.Find<Button>(this).Single(b=>AutomationProperties.GetName(b)==name);
        UiPathTests.Click(Chrome("Maximize / restore"));Check("sharedMaximizeButtonMaximizesItsActualWindow",WindowState==WindowState.Maximized);
        UiPathTests.Click(Chrome("Maximize / restore"));Check("sharedRestoreButtonReturnsItsActualWindowToNormal",WindowState==WindowState.Normal);
        UiPathTests.Click(Chrome("Minimize"));Check("sharedMinimizeButtonMinimizesItsActualWindow",WindowState==WindowState.Minimized);WindowState=WindowState.Normal;
        UiPathTests.Click(Chrome("Close window"));Check("sharedCloseButtonClosesOnlyItsActualFixture",!IsVisible);
        await Task.CompletedTask;
    }
}
