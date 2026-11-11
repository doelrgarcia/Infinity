using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid,List<ImageInput>> imageDrafts=[];
    // Offline UI tests provide synthetic clipboard input without reading or
    // replacing the user's real clipboard. Normal requests use Windows below.
    private Func<BitmapSource?>? clipboardImageTest;
    private readonly WrapPanel imageTray=new();
    private List<ImageInput> DraftImages=>imageDrafts.TryGetValue(conversation.Id,out var images)?images:imageDrafts[conversation.Id]=[];
    internal static BitmapSource ImagePreview(ImageInput input)
    {
        using var stream=new MemoryStream(input.Bytes());var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];if(frame.PixelWidth>10000||frame.PixelHeight>10000||(long)frame.PixelWidth*frame.PixelHeight>20_000_000)throw new IOException("Screenshot dimensions exceed 20 million pixels.");stream.Position=0;var preview=new BitmapImage();preview.BeginInit();preview.CacheOption=BitmapCacheOption.OnLoad;preview.DecodePixelWidth=Math.Min(1024,frame.PixelWidth);preview.StreamSource=stream;preview.EndInit();preview.Freeze();return preview;
    }
    private void AddImage(ImageInput image)
    {
        if(busy)throw new IOException("Wait for the current reply before attaching screenshots.");
        if(DraftImages.Any(i=>i.Sha256==image.Sha256))return;
        ImagePreview(image);ImageInput.All(conversation.Messages.Concat([new("user","",Images:DraftImages.Append(image).ToArray())]));DraftImages.Add(image);RefreshImageTray();
    }
    private void PickImage()
    {
        try{if(busy)return;var picker=new Microsoft.Win32.OpenFileDialog{Filter="Screenshots|*.png;*.jpg;*.jpeg",Multiselect=false,Title="Attach screenshot"};if(picker.ShowDialog(this)==true){var file=new FileInfo(picker.FileName);if(file.Length>ImageInput.MaximumBytes)throw new IOException("Screenshot exceeds 2 MiB.");AddImage(ImageInput.FromBytes(file.Name,File.ReadAllBytes(file.FullName)));}}
        catch(Exception e) when(e is IOException or ArgumentException or NotSupportedException){status.Text=e.Message;}
    }
    private void PasteImage()
    {
        try{if(busy)return;var bitmap=clipboardImageTest!=null?clipboardImageTest():Clipboard.GetImage();if(bitmap==null){status.Text="The clipboard does not contain an image. Copy an image, then choose Paste screenshot.";return;}var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);AddImage(ImageInput.FromBytes("Clipboard.png",stream.ToArray()));}
        catch(Exception e){status.Text="Could not attach screenshot: "+e.Message;}
    }
    private void RefreshImageTray()
    {
        imageTray.Children.Clear();foreach(var image in DraftImages.ToArray())
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4)};row.Children.Add(new Image{Source=ImagePreview(image),Width=64,Height=60,ToolTip=image.Name});row.Children.Add(new TextBlock{Text=image.Name,MaxWidth=140,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6)});
            var remove=new Button{Content="Remove",MaxWidth=80,ToolTip=image.Name+" · "+image.Sha256};remove.Click+=(_,_)=>{if(!busy){DraftImages.Remove(image);RefreshImageTray();}};row.Children.Add(remove);imageTray.Children.Add(row);
        }
        RefreshFileTray();imageTray.Visibility=imageTray.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
    }
}
