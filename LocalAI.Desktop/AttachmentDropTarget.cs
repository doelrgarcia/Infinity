using System.IO;
using System.Windows;

namespace LocalAI.Desktop;

// File drops create reviewed attachment snapshots, just like the file picker.
// They never move the original files, change folder access, or send a request.
internal static class AttachmentDropTarget
{
    internal const string Hint="Drop files and images here to attach, then review and press Send.";

    internal static void Register(UIElement target,Func<bool> available,Action<string[]> attach,Action<string> report)
    {
        target.AllowDrop=true;
        void Hover(object sender,DragEventArgs e)
        {
            try
            {
                if(!e.Data.GetDataPresent(DataFormats.FileDrop,false))return;
                e.Handled=true;e.Effects=available()&&(e.AllowedEffects&DragDropEffects.Copy)!=0?DragDropEffects.Copy:DragDropEffects.None;
            }
            catch{e.Handled=true;e.Effects=DragDropEffects.None;}
        }
        target.AddHandler(DragDrop.PreviewDragEnterEvent,new DragEventHandler(Hover),true);
        target.AddHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler(Hover),true);
        target.AddHandler(DragDrop.PreviewDropEvent,new DragEventHandler((_,e)=>
        {
            try
            {
                if(!e.Data.GetDataPresent(DataFormats.FileDrop,false))return;
                e.Handled=true;e.Effects=DragDropEffects.None;
                if(!available())throw new IOException("Wait or stop the current reply or dictation before attaching files.");
                if((e.AllowedEffects&DragDropEffects.Copy)==0)throw new IOException("Drop files as a copy to attach them. Original files stay in place.");
                if(e.Data.GetData(DataFormats.FileDrop,false) is not string[] paths||paths.Length==0)throw new IOException("Drop readable files or images to attach.");
                if(paths.Length>8)throw new IOException("Attach at most four documents and four images at a time.");
                if(paths.Any(Directory.Exists))throw new IOException("To connect a folder, choose Files on computer and network. Drop individual files or images to attach them.");
                attach(paths);e.Effects=DragDropEffects.Copy;
            }
            catch(Exception error)
            {
                e.Handled=true;e.Effects=DragDropEffects.None;
                report("Could not attach dropped files: "+(error is IOException?error.Message:"Choose supported, readable documents or images."));
            }
        }),true);
    }
}
