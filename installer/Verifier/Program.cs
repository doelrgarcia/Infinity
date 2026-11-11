using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Infinity.Installation;

internal static class Program
{
    [STAThread] private static void Main()
    {
        var app=new Application();var window=new Window{Title="Infinity - verify installation",Width=720,Height=520,Background=new SolidColorBrush(Color.FromRgb(17,21,30)),Foreground=Brushes.White,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var body=new DockPanel{Margin=new Thickness(24)};window.Content=body;
        var heading=new TextBlock{Text="Checking installed files…",FontSize=25,Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(heading,Dock.Top);body.Children.Add(heading);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);body.Children.Add(buttons);var copy=new Button{Content="Copy results",Padding=new Thickness(14,7,14,7),IsEnabled=false};buttons.Children.Add(copy);var close=new Button{Content="Close",Padding=new Thickness(14,7,14,7),Margin=new Thickness(10,0,0,0)};buttons.Children.Add(close);close.Click+=(_,_)=>window.Close();
        var report=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=window.Background,Foreground=window.Foreground,BorderThickness=new Thickness(0)};body.Children.Add(report);copy.Click+=(_,_)=>Clipboard.SetText(report.Text);
        window.Loaded+=async(_,_)=>
        {
            try
            {
                var summary=await Task.Run(()=>Verify());heading.Text="Installation verified";report.Text=JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true});
            }
            catch(Exception error){heading.Text="Installation needs attention";report.Text=error is IOException or InvalidDataException?error.Message:"Verification could not finish. Close Infinity and try again.";}
            copy.IsEnabled=true;
        };app.Run(window);
    }

    internal static object Verify(string? directory=null)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory??AppContext.BaseDirectory))+Path.DirectorySeparatorChar;using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"installation-manifest.json")));var revision=manifest.RootElement.GetProperty("SourceRevision").GetString();
        if(revision==null||revision.Length!=40||revision.Any(c=>!char.IsAsciiHexDigit(c)))throw new InvalidDataException("Invalid installation manifest.");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var count=0;
        foreach(var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
        {
            var relative=entry.GetProperty("path").GetString()??"";var full=Path.GetFullPath(Path.Combine(root,relative));
            if(relative.Length==0||Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p=="..")||!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!seen.Add(relative.Replace('\\','/'))||Path.GetFileName(full).Equals("appsettings.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe or duplicate manifest member.");
            var ancestors=new List<string>();for(var part=full;part!=null;part=Path.GetDirectoryName(part))ancestors.Add(part);
            foreach(var part in ancestors.AsEnumerable().Reverse())if(File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Linked installation files are excluded.");
            using var stream=File.OpenRead(full);if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new IOException("An installed application file differs: "+relative);count++;
        }
        if(count==0||!seen.Contains("LocalAI.Desktop.exe")||!seen.Contains("Infinity.Verify.exe"))throw new InvalidDataException("Incomplete installation manifest.");
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"appsettings.json")));var models=config.RootElement.GetProperty("Models");if(models.ValueKind!=JsonValueKind.Array||models.GetArrayLength()==0)throw new InvalidDataException("No model profiles are configured.");
        return new{Passed=true,SourceRevision=revision,ApplicationFilesVerified=count,SettingsJsonParsed=true,PrivateSettingsValuesPrinted=false,Scope="Installed application bytes and configuration syntax only. This does not send account requests or test model inference."};
    }
}
