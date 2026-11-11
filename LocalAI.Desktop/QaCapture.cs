using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalAI.Desktop;
internal static class QaCapture
{
    internal static void Save(FrameworkElement element,string path)
    {
        element.UpdateLayout();var width=Math.Max(1,(int)element.ActualWidth);var height=Math.Max(1,(int)element.ActualHeight);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))png.Save(stream);
        var pixels=new byte[checked(width*height*4)];bitmap.CopyPixels(pixels,width*4,0);var colors=new HashSet<int>();var visible=0;var white=0;var run=0;var longestRun=0;
        for(var i=0;i<pixels.Length;i+=4)
        {
            if(i%(width*4)==0)run=0;
            if(pixels[i+3]!=0){visible++;if(colors.Count<8)colors.Add(BitConverter.ToInt32(pixels,i));}
            if(pixels[i+3]>=250&&pixels[i]>=245&&pixels[i+1]>=245&&pixels[i+2]>=245){white++;longestRun=Math.Max(longestRun,++run);}else run=0;
        }
        if(longestRun>=48||white>width*height/100)throw new IOException("QA render contains a white background region: "+path);
        if(visible<width*height/100||colors.Count<4)throw new IOException("QA render is blank or transparent. Restore an interactive graphics session and rerun; this is not passing visual evidence. Capture: "+path);
    }
}
