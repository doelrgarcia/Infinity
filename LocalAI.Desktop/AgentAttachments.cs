using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    internal void PreviewAttachment(FileAttachment file)=>ShowDocument(file.Text,file.Name);
}
internal sealed partial class AgentView
{
    private readonly Dictionary<Guid,(List<FileAttachment> Files,List<ImageInput> Images)> attachments=[];
    private readonly WrapPanel attachmentTray=new();
    private void InitializeAttachmentDrop()
    {
        composer.ToolTip=AttachmentDropTarget.Hint;
        AttachmentDropTarget.Register(this,()=>!sending&&!host.AgentIsWorking&&voice==null,paths=>
        {
            AddAttachments(paths);notice.Text="Attachments ready. Review or remove them before sending.";
        },message=>notice.Text=message);
    }
    private (List<FileAttachment> Files,List<ImageInput> Images) AttachmentDraft
    {
        get{var key=state.DraftTaskId??Guid.Empty;if(!attachments.TryGetValue(key,out var value))attachments[key]=value=([],[]);return value;}
    }
    private void PickAgentAttachments()
    {
        try
        {
            if(sending||host.AgentIsWorking)throw new IOException("Wait or stop the current reply before attaching files.");
            var paths=host.PickAttachmentPaths();if(paths!=null)AddAttachments(paths);
        }
        catch(Exception e){notice.Text="Could not attach files: "+(e is IOException?e.Message:"The file could not be read. Choose a supported, readable document or image.");}
    }
    internal void AddAttachments(IEnumerable<string> paths)
    {
        var batch=MainWindow.ReadAttachments(paths);var draft=AttachmentDraft;var files=draft.Files.Concat(batch.Files).DistinctBy(f=>(f.Name,f.Sha256)).ToArray();var images=draft.Images.Concat(batch.Images).DistinctBy(i=>i.Sha256).ToArray();FileAttachment.ValidateDraft(files);ImageInput.All([new("user","",Images:images)]);draft.Files.Clear();draft.Files.AddRange(files);draft.Images.Clear();draft.Images.AddRange(images);RefreshAttachments();
    }
    internal (int Files,int Images) TestAttachmentCounts=>(AttachmentDraft.Files.Count,AttachmentDraft.Images.Count);
    private void RefreshAttachments()
    {
        attachmentTray.Children.Clear();var draft=AttachmentDraft;
        foreach(var file in draft.Files.ToArray()){var view=new Button{Content=file.Name+" · text"};view.Click+=(_,_)=>host.PreviewAttachment(file);attachmentTray.Children.Add(view);var remove=new Button{Content="Remove",ToolTip=file.Name};remove.Click+=(_,_)=>{if(!sending){draft.Files.Remove(file);RefreshAttachments();}};attachmentTray.Children.Add(remove);}
        foreach(var image in draft.Images.ToArray()){attachmentTray.Children.Add(new Image{Source=MainWindow.ImagePreview(image),Width=64,Height=50,ToolTip=image.Name});attachmentTray.Children.Add(new TextBlock{Text=image.Name,MaxWidth=140,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});var remove=new Button{Content="Remove",ToolTip=image.Name};remove.Click+=(_,_)=>{if(!sending){draft.Images.Remove(image);RefreshAttachments();}};attachmentTray.Children.Add(remove);}
    }
}
