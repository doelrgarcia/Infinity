using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid,List<FileAttachment>> fileDrafts=[];
    internal Func<string[]?>? AttachmentPathsTest {get;set;}
    internal string[]? PickAttachmentPaths()
    {
        if(AttachmentPathsTest!=null)return AttachmentPathsTest();
        var picker=AttachmentPicker();return picker.ShowDialog(this)==true?picker.FileNames:null;
    }
    private List<FileAttachment> DraftFiles=>fileDrafts.TryGetValue(conversation.Id,out var files)?files:fileDrafts[conversation.Id]=[];
    internal static (FileAttachment[] Files,ImageInput[] Images) ReadAttachments(IEnumerable<string> paths)
    {
        var files=new List<FileAttachment>();var images=new List<ImageInput>();foreach(var path in paths)
        {
            if(Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            {
                for(var p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if(File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Linked attachment paths are excluded.");
                var info=new FileInfo(path);if(info.Length>ImageInput.MaximumBytes)throw new IOException("Images must be at most 2 MiB each.");var image=ImageInput.FromBytes(info.Name,File.ReadAllBytes(path));ImagePreview(image);images.Add(image);
            }
            else files.Add(FileAttachment.Read(path));
        }
        return(files.DistinctBy(f=>(f.Name,f.Sha256)).ToArray(),images.DistinctBy(i=>i.Sha256).ToArray());
    }
    internal static Microsoft.Win32.OpenFileDialog AttachmentPicker()=>new(){Title="Attach files and images",Multiselect=true,Filter="Documents, text and images|*.pdf;*.docx;*.txt;*.md;*.csv;*.json;*.xml;*.yaml;*.yml;*.cs;*.js;*.ts;*.py;*.sql;*.html;*.css;*.png;*.jpg;*.jpeg|All files|*.*"};
    private void AttachPaths(IEnumerable<string> paths)
    {
        if(busy)throw new IOException("Wait for the current reply before attaching files.");var batch=ReadAttachments(paths);var documents=DraftFiles.Concat(batch.Files).DistinctBy(f=>(f.Name,f.Sha256)).ToArray();var images=DraftImages.Concat(batch.Images).DistinctBy(i=>i.Sha256).ToArray();FileAttachment.ValidateDraft(documents);ImageInput.All(conversation.Messages.Concat([new("user","",Images:images)]));
        DraftFiles.Clear();DraftFiles.AddRange(documents);DraftImages.Clear();DraftImages.AddRange(images);RefreshImageTray();status.Text="Attachments ready. Review or remove them before sending. Documents send extracted text; images require Codex.";
    }
    private void PickAttachments()
    {
        try{var paths=PickAttachmentPaths();if(paths!=null)AttachPaths(paths);}
        catch(Exception e){status.Text="Could not attach files: "+(e is IOException?e.Message:"Choose a supported, readable document or image.");}
    }
    private void RefreshFileTray()
    {
        foreach(var file in DraftFiles.ToArray())
        {
            var row=new WrapPanel{Margin=new Thickness(4)};var preview=new Button{Content=file.Name+" · extracted text",ToolTip=file.Text.Length+" characters"};preview.Click+=(_,_)=>ShowDocument(file.Text,file.Name);row.Children.Add(preview);var remove=new Button{Content="Remove",ToolTip=file.Name};remove.Click+=(_,_)=>{if(!busy){DraftFiles.Remove(file);RefreshImageTray();}};row.Children.Add(remove);imageTray.Children.Add(row);
        }
    }
    private void RenderFileAttachments(Panel panel,Message message)
    {
        foreach(var file in message.Files??[]){var view=new Button{Content="Attached: "+file.Name+" · view extracted text",HorizontalAlignment=HorizontalAlignment.Left};view.Click+=(_,_)=>ShowDocument(file.Text,file.Name);panel.Children.Add(view);}
    }
}
