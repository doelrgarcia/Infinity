using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalAI.Desktop;
internal sealed class WorkingFilesPanel : DockPanel
{
    private readonly StackPanel rows=new();
    private readonly Dictionary<string,Button> buttons=new(StringComparer.OrdinalIgnoreCase);
    private string? selectedPath;
    internal IReadOnlyCollection<string> DisplayedPaths=>buttons.Keys;
    internal int FolderCount {get;private set;}
    public WorkingFilesPanel()
    {
        var heading=new TextBlock{Text="Files in this chat",FontSize=12,Foreground=Color("#98A4B7"),Margin=new Thickness(0,0,0,8),ToolTip="Included source snapshots and generated code saved during this app session. Full paths appear on hover."};SetDock(heading,Dock.Top);Children.Add(heading);
        Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
    }
    private static SolidColorBrush Color(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    internal void Update(IEnumerable<string> sources,IEnumerable<string> saved,Action<string,bool> select)
    {
        var included=sources.ToHashSet(StringComparer.OrdinalIgnoreCase);var written=saved.ToHashSet(StringComparer.OrdinalIgnoreCase);rows.Children.Clear();buttons.Clear();FolderCount=0;
        foreach(var group in included.Concat(written).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).GroupBy(p=>Path.GetDirectoryName(p)??"",StringComparer.OrdinalIgnoreCase))
        {
            FolderCount++;var members=new StackPanel{Margin=new Thickness(10,2,0,12)};
            var folderLabel=new DockPanel();var count=new TextBlock{Text=group.Count().ToString(),Foreground=Color("#7F8A9D"),FontSize=11,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};SetDock(count,Dock.Right);folderLabel.Children.Add(count);
            var folderIcon=new Image{Source=UiIcons.Source("folder"),Width=16,Height=16,Margin=new Thickness(0,0,7,0)};SetDock(folderIcon,Dock.Left);folderLabel.Children.Add(folderIcon);folderLabel.Children.Add(new TextBlock{Text=Path.GetFileName(group.Key.TrimEnd(Path.DirectorySeparatorChar)) is {Length:>0} name?name:group.Key,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12,Foreground=Color("#C7D2E5")});
            var folder=new Button{Content=folderLabel,ToolTip=group.Key,Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(5),Margin=new Thickness(0,6,0,0)};System.Windows.Automation.AutomationProperties.SetName(folder,"Folder: "+group.Key+". Toggle files");folder.Click+=(_,_)=>members.Visibility=members.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;rows.Children.Add(folder);rows.Children.Add(members);
            foreach(var path in group)
            {
                var row=new DockPanel();var kind=new TextBlock{Text=included.Contains(path)?written.Contains(path)?"context + saved":"context":"saved",Foreground=Color("#7F8A9D"),FontSize=10,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center};SetDock(kind,Dock.Right);row.Children.Add(kind);var icon=new Image{Source=UiIcons.Source("file"),Width=14,Height=14,Margin=new Thickness(0,0,7,0)};SetDock(icon,Dock.Left);row.Children.Add(icon);row.Children.Add(new TextBlock{Text=Path.GetFileName(path),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});
                var button=new Button{Content=row,ToolTip=path,HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,1,0,1),Padding=new Thickness(7,5,7,5),Background=Color(string.Equals(selectedPath,path,StringComparison.OrdinalIgnoreCase)?"#25314B":"#101318"),BorderBrush=Brushes.Transparent};System.Windows.Automation.AutomationProperties.SetName(button,"Working file: "+path);button.Click+=(_,_)=>{selectedPath=path;foreach(var pair in buttons)pair.Value.Background=Color(pair.Key.Equals(path,StringComparison.OrdinalIgnoreCase)?"#25314B":"#101318");select(path,included.Contains(path));};members.Children.Add(button);buttons.Add(path,button);
            }
        }
        if(buttons.Count==0)
        {
            var empty=new StackPanel{Margin=new Thickness(10,36,10,10)};
            empty.Children.Add(new Image{Source=UiIcons.Source("folder"),Width=38,Height=38,Opacity=0.8,Margin=new Thickness(0,0,0,14)});
            empty.Children.Add(new TextBlock{Text="No files yet",FontSize=15,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,Foreground=Color("#C7D2E5")});
            empty.Children.Add(new TextBlock{Text="Add files or a folder to give Infinity context.",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=Color("#98A4B7"),FontSize=12,Margin=new Thickness(0,8,0,0)});rows.Children.Add(empty);
        }
    }
    internal void TestSelect(string path)=>buttons[path].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
}
