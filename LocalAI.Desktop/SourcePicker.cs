using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LocalAI.Desktop;
internal sealed class SourcePicker : Window
{
    private readonly TextBox path=new();
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ListBox entries=new(){SelectionMode=SelectionMode.Extended};
    private string current=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private readonly Button go=new(){Content=UiIcons.Label("Open path","folder"),Margin=new Thickness(8,0,0,0)};
    private readonly Button up=new(){Content=UiIcons.Label("Parent","folder"),Margin=new Thickness(0,0,8,0)};
    private readonly Button choose=new(){Margin=new Thickness(0,8,0,0)};
    private readonly bool folderOnly;
    public string[] SelectedPaths {get;private set;}=[];
    private sealed record Entry(string Path,bool Folder){public string DisplayName=>System.IO.Path.GetFileName(Path);public System.Windows.Media.ImageSource Icon=>UiIcons.Source(Folder?"folder":"file");public override string ToString()=>DisplayName;}
    public SourcePicker(bool folder,string? initialDirectory=null)
    {
        folderOnly=folder;
        if(initialDirectory!=null)current=initialDirectory;
        Title=folder?"Select source folder":"Select source files";Width=780;Height=560;MinWidth=500;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var panel=new DockPanel{Margin=new Thickness(18)};
        var address=new DockPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(address,Dock.Top);panel.Children.Add(address);
        System.Windows.Automation.AutomationProperties.SetName(go,"Open path");
        System.Windows.Automation.AutomationProperties.SetName(up,"Parent folder");
        System.Windows.Automation.AutomationProperties.SetName(path,"Folder path");
        System.Windows.Automation.AutomationProperties.SetName(entries,"Source folders and files");
        System.Windows.Automation.AutomationProperties.SetAutomationId(error,"SourcePickerStatus");
        error.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)){Source=error});
        go.Click+=(_,_)=>Load(path.Text);DockPanel.SetDock(go,Dock.Right);address.Children.Add(go);
        if(folder){var browse=new Button{Content=UiIcons.Label("Choose folder","folder"),Margin=new Thickness(8,0,0,0)};System.Windows.Automation.AutomationProperties.SetName(browse,"Choose folder in Windows Explorer");browse.Click+=(_,_)=>{var picked=FolderPicker.Choose(this,current);if(picked!=null)Load(picked);};DockPanel.SetDock(browse,Dock.Right);address.Children.Add(browse);}
        up.Click+=(_,_)=>Load(Directory.GetParent(current)?.FullName??current);DockPanel.SetDock(up,Dock.Left);address.Children.Add(up);address.Children.Add(path);path.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Load(path.Text);};
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);bottom.Children.Add(error);
        choose.Content=UiIcons.Label(folder?"Include this folder":"Include selected files",folder?"folder":"file");
        System.Windows.Automation.AutomationProperties.SetName(choose,folder?"Include this folder":"Include selected files");
        choose.Click+=(_,_)=>
        {
            SelectedPaths=[];
            if(!Load(path.Text,true))return;
            SelectedPaths=folder?[current]:entries.SelectedItems.Cast<Entry>().Where(e=>!e.Folder).Select(e=>e.Path).ToArray();
            if(SelectedPaths.Length>0)DialogResult=true;else error.Text="Select one or more source files.";
        };bottom.Children.Add(choose);
        entries.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'><Image Source='{Binding Icon}' Width='20' Height='20' Margin='0,0,10,0'/><TextBlock Text='{Binding DisplayName}' VerticalAlignment='Center'/></StackPanel></DataTemplate>");
        entries.MouseDoubleClick+=(_,_)=>{if(entries.SelectedItem is Entry {Folder:true} e)Load(e.Path);};panel.Children.Add(entries);DarkTheme.Wrap(this,panel);Load(current);
    }
    internal bool TestNavigate(string directory)
    {path.Text=directory;go.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var selected=current;up.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var parent=current;path.Text=directory;go.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));return selected==Path.GetFullPath(directory)&&parent==Directory.GetParent(directory)?.FullName;}
    internal void TestChoose(params string[] fileNames)
    {TestSelectFiles(fileNames);TestConfirm();}
    internal void TestSelectFiles(params string[] fileNames)
    {entries.SelectedItems.Clear();foreach(var entry in entries.Items.Cast<Entry>().Where(e=>fileNames.Contains(e.DisplayName)))entries.SelectedItems.Add(entry);}
    internal void TestSetAddress(string directory)=>path.Text=directory;
    internal void TestConfirm()=>choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal string TestStatus=>error.Text;
    internal string TestAccessibleStatus=>System.Windows.Automation.AutomationProperties.GetName(error);
    internal string[] TestVisibleFiles=>entries.Items.Cast<Entry>().Where(e=>!e.Folder).Select(e=>e.DisplayName).ToArray();
    private bool Load(string directory,bool preserveSelection=false)
    {
        try
        {
            var selected=preserveSelection?entries.SelectedItems.Cast<Entry>().Select(e=>e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase):[];
            directory=Path.GetFullPath(directory.Trim());var items=Directory.EnumerateDirectories(directory).Select(p=>new Entry(p,true)).OrderBy(e=>e.Path).Concat(Directory.EnumerateFiles(directory).Where(LocalAI.Core.ContextFiles.Allowed).Select(p=>new Entry(p,false)).OrderBy(e=>e.Path)).Take(2000).ToArray();
            entries.ItemsSource=items;current=directory;path.Text=current;
            foreach(var item in items.Where(e=>selected.Contains(e.Path)))entries.SelectedItems.Add(item);
            error.Text=folderOnly?"Double-click folders to navigate. Include this folder uses the folder shown in the address field.":"Double-click folders to navigate. Hold Ctrl to select multiple source files. Configuration and generated folders are excluded during import.";
            return true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){error.Text=e.Message;return false;}
    }
}
