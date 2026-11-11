using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalAI.Desktop;
internal static class UiIcons
{
    private static SolidColorBrush Color(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    public static ImageSource Source(string kind)
        =>Cache.GetOrAdd(kind,Build);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,ImageSource> Cache=new();
    private static ImageSource Build(string kind)
    {
        var group=new DrawingGroup();
        void Shape(string path,string fill)=>group.Children.Add(new GeometryDrawing(Color(fill),null,Geometry.Parse(path)));
        switch(kind)
        {
            case "microphone":Shape("M9,2 Q9,0 12,0 Q15,0 15,2 L15,11 Q15,14 12,14 Q9,14 9,11 Z M5,9 L7,9 L7,11 Q7,17 12,17 Q17,17 17,11 L17,9 L19,9 L19,11 Q19,18 13,19 L13,22 L17,22 L17,24 L7,24 L7,22 L11,22 L11,19 Q5,18 5,11 Z","#AFC2E7");break;
            case "stop":Shape("M5,5 L19,5 L19,19 L5,19 Z","#F2A4AF");break;
            case "folder":Shape("M1,5 L1,3 Q1,2 2,2 L9,2 L12,5 L22,5 Q23,5 23,7 L23,19 Q23,21 21,21 L3,21 Q1,21 1,19 Z","#B98A37");Shape("M2,8 L23,8 L21,21 L2,21 Z","#E4B658");break;
            case "file":Shape("M5,1 L15,1 L21,7 L21,23 L5,23 Z","#739ECC");Shape("M15,1 L15,7 L21,7 Z","#BCD8ED");Shape("M8,11 L18,11 L18,13 L8,13 Z M8,16 L18,16 L18,18 L8,18 Z","#263247");break;
            case "save":Shape("M2,2 L18,2 L22,6 L22,22 L2,22 Z","#6ECFC2");Shape("M6,2 L16,2 L16,9 L6,9 Z M6,14 L18,14 L18,22 L6,22 Z","#263247");break;
            case "copy":Shape("M3,2 L17,2 L17,5 L6,5 L6,19 L3,19 Z M8,7 L22,7 L22,23 L8,23 Z","#88AFD4");break;
            case "web":group.Children.Add(new GeometryDrawing(Color("#6ECFC2"),null,new EllipseGeometry(new Point(12,12),11,11)));Shape("M11,1 L13,1 L13,23 L11,23 Z M1,11 L23,11 L23,13 L1,13 Z","#263247");group.Children.Add(new GeometryDrawing(null,new Pen(Color("#263247"),1.5),new EllipseGeometry(new Point(12,12),5,11)));break;
            case "cloud":Shape("M6,20 C-1,20 -1,11 5,10 C4,1 17,0 19,8 C26,8 27,20 19,20 Z","#A7A0E4");break;
            case "clear":Shape("M7,6 L19,6 L18,22 L8,22 Z M5,2 L20,2 L20,5 L5,5 Z","#C88D98");break;
            default:Shape("M10,2 L14,2 L14,10 L22,10 L22,14 L14,14 L14,22 L10,22 L10,14 L2,14 L2,10 L10,10 Z","#6ECFC2");break;
        }
        var drawing=new DrawingImage(group);drawing.Freeze();return drawing;
    }
    public static object Label(string text,string kind)
    {var panel=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(new Image{Source=Source(kind),Width=17,Height=17,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});panel.Children.Add(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center});return panel;}
    public static string ForLabel(string label)=>label.Contains("folder",StringComparison.OrdinalIgnoreCase)?"folder":label.Contains("file",StringComparison.OrdinalIgnoreCase)?"file":label.Contains("web",StringComparison.OrdinalIgnoreCase)?"web":label.Contains("AI apps",StringComparison.OrdinalIgnoreCase)?"cloud":label.Contains("Clear",StringComparison.OrdinalIgnoreCase)||label.Contains("Remove",StringComparison.OrdinalIgnoreCase)?"clear":"new";
}
