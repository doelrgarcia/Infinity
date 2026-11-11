using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal sealed class FileActionReviewWindow : Window
{
    private readonly FileWorkspace workspace;
    private readonly FileAction action;
    private readonly TextBox before=TextArea(),after=TextArea();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button preview=new(){Content="Preview exact action"},apply=new(){Content="Confirm reviewed action",IsEnabled=false};
    private FileActionPlan? plan;
    private bool busy;
    public FileActionReceipt? Receipt {get;private set;}
    internal bool Headless {get;set;}
    internal Action<FileActionPlan>? BeforeApply {get;set;}
    internal static TextBox TextArea()=>new(){AcceptsReturn=true,AcceptsTab=true,IsReadOnly=true,FontFamily=new System.Windows.Media.FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10)};
    public FileActionReviewWindow(FileWorkspace workspace,FileAction action)
    {
        this.workspace=workspace;this.action=action;Title="Review file action — "+action.Operation;Width=1050;Height=680;MinWidth=600;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var root=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=$"{action.Operation.ToUpperInvariant()}\n{action.Path}"+(action.Destination==null?"":"\nTo: "+action.Destination),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        top.Children.Add(new TextBlock{Text="Review paths and changes. Existing writes retain a backup; moves and deletes retain a recovery copy. Each action is confirmed separately.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});top.Children.Add(preview);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);bottom.Children.Add(status);bottom.Children.Add(apply);root.Children.Add(bottom);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());var left=new DockPanel{Margin=new Thickness(0,10,6,10)};var right=new DockPanel{Margin=new Thickness(6,10,0,10)};var oldLabel=new TextBlock{Text="BEFORE / CURRENT",Margin=new Thickness(0,0,0,6)};var newLabel=new TextBlock{Text="AFTER / PROPOSED",Margin=new Thickness(0,0,0,6)};DockPanel.SetDock(oldLabel,Dock.Top);DockPanel.SetDock(newLabel,Dock.Top);left.Children.Add(oldLabel);left.Children.Add(before);right.Children.Add(newLabel);right.Children.Add(after);Grid.SetColumn(right,1);grid.Children.Add(left);grid.Children.Add(right);root.Children.Add(grid);DarkTheme.Wrap(this,root);
        preview.Click+=async(_,_)=>await Preview();apply.Click+=async(_,_)=>await Apply();Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Waiting for the Windows file operation to finish. No process will be killed.";}};
    }
    private async Task Preview()
    {
        busy=true;preview.IsEnabled=false;apply.IsEnabled=false;plan=null;status.Text="Checking the connected path…";
        try{plan=await Task.Run(()=>workspace.Preview(action));before.Text=plan.Before;after.Text=plan.After;status.Text=$"Exact target: {plan.Source}"+(plan.Destination==null?"":"\nDestination: "+plan.Destination);apply.IsEnabled=true;}
        catch(Exception e){status.Text=e.Message;}
        finally{busy=false;preview.IsEnabled=true;}
    }
    private async Task Apply()
    {
        if(plan==null)return;busy=true;apply.IsEnabled=false;preview.IsEnabled=false;status.Text="Applying the reviewed action…";
        try{BeforeApply?.Invoke(plan);Receipt=await Task.Run(()=>workspace.Apply(plan,true));busy=false;if(!Headless)DialogResult=true;}
        catch(Exception e){plan=null;status.Text=e.Message;}
        finally{busy=false;preview.IsEnabled=true;}
    }
    internal async Task TestPreview()=>await Preview();
    internal async Task TestApply()=>await Apply();
    internal bool HasReviewedPlan=>plan!=null&&apply.IsEnabled;
}

internal sealed class FileWorkspaceWindow : Window
{
    private readonly List<string> roots;
    private readonly Action rootsChanged;
    private readonly Action<ContextFile> include;
    private readonly Func<FileActionReceipt,Task> changed;
    private readonly ComboBox locations=new(){MinWidth=180};
    private readonly TextBox connectPath=new(){MinWidth=200},address=new(),query=new(),target=new(),destination=new(),editor=FileActionReviewWindow.TextArea();
    private readonly ListBox entries=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly List<Button> controls=[];
    private string current="";
    private bool busy,closed,changing;
    private Task? inFlight;
    internal Action<FileActionReviewWindow>? ReviewTestAction;
    private readonly CancellationTokenSource lifetime=new();
    private FileWorkspace Workspace=>new(roots);
    public FileWorkspaceWindow(List<string> roots,Action rootsChanged,Action<ContextFile> include,Func<FileActionReceipt,Task> changed)
    {
        this.roots=roots;this.rootsChanged=rootsChanged;this.include=include;this.changed=changed;
        Title="Files on this computer and network";Width=1120;Height=740;MinWidth=650;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);editor.IsReadOnly=false;
        var root=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=@"Connect the folder you want to work in, a mapped drive or a \\server\share path. Your selected AI (including local models) can read and write screened files inside this chat's connected folders. Overwrites keep backups; deletes, moves and copies require review. Disconnect a folder to remove access. Cloud requests still show the exact outgoing content before sending. Windows account permissions apply.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        var connect=new DockPanel();Add(connect,"Connect folder",async()=>await Connect());AddFolderButton(connect,connectPath);connect.Children.Add(connectPath);top.Children.Add(connect);
        var locationsRow=new DockPanel{Margin=new Thickness(0,6,0,6)};Add(locationsRow,"Disconnect",()=>{if(locations.SelectedItem is string p){roots.Remove(p);rootsChanged();RefreshRoots();entries.Items.Clear();current="";}return Task.CompletedTask;});locationsRow.Children.Add(locations);top.Children.Add(locationsRow);locations.SelectionChanged+=async(_,_)=>{if(!busy&&locations.SelectedItem is string p)await Navigate(p);};
        var nav=new DockPanel();Add(nav,"Go",async()=>await Navigate(address.Text));Add(nav,"Up",async()=>{if(current.Length>0)await Navigate(Path.GetDirectoryName(current)??current);});var folderNav=FolderPicker.Button(this,address,"Choose workspace folder",onChosen:path=>{if(!busy&&!changing)_=Navigate(path);});DockPanel.SetDock(folderNav,Dock.Right);controls.Add(folderNav);nav.Children.Add(folderNav);nav.Children.Add(address);top.Children.Add(nav);address.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)await Navigate(address.Text);};
        var search=new DockPanel{Margin=new Thickness(0,6,0,8)};Add(search,"Search names",async()=>await Run(async()=>{var path=current;var term=query.Text;var result=await Task.Run(()=>Workspace.Search(path,term,lifetime.Token));ShowEntries(result);status.Text=$"{result.Count} matching entries.";}));search.Children.Add(query);top.Children.Add(search);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(new TextBlock{Text="Selected / new file path"});bottom.Children.Add(target);bottom.Children.Add(new TextBlock{Text="Copy / move destination (new name, no overwrite)"});bottom.Children.Add(destination);
        var actions=new WrapPanel();bottom.Children.Add(actions);
        Add(actions,"Read / preview",async()=>await Read(false));Add(actions,"Include in chat",async()=>await Read(true));Add(actions,"New file draft",()=>{target.Text=Path.Combine(current,"NewFile.txt");editor.Text="";status.Text="New file draft; choose a name and review the write.";return Task.CompletedTask;});
        Add(actions,"Review write",async()=>await Review(new("write",target.Text,Content:editor.Text)));Add(actions,"New folder",async()=>await Review(new("mkdir",target.Text)));
        Add(actions,"Copy",async()=>await Review(new("copy",target.Text,destination.Text)));Add(actions,"Move / rename",async()=>await Review(new("move",target.Text,destination.Text)));Add(actions,"Delete with recovery",async()=>await Review(new("delete",target.Text)));bottom.Children.Add(status);
        Add(actions,"Recovery files",async()=>await Run(async()=>{var folder=current;var rows=await Task.Run(()=>Workspace.ListRecovery(folder));ShowEntries(rows);status.Text="Select a recovery item, set a new destination, then choose Restore. Existing files are never overwritten.";}));
        Add(actions,"Restore",async()=>await Review(new("restore",target.Text,destination.Text)));
        Add(actions,"Source control & Teams",()=>{new SourceControlWindow(roots,current,target.Text){Owner=this}.ShowDialog();return Task.CompletedTask;});
        Add(actions,"Run commands",()=>{new CommandWindow(roots,new("",current)){Owner=this}.ShowDialog();return Task.CompletedTask;});
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(0.45,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(0.55,GridUnitType.Star)});entries.Margin=new Thickness(0,0,8,8);editor.Margin=new Thickness(8,0,0,8);Grid.SetColumn(editor,1);grid.Children.Add(entries);grid.Children.Add(editor);root.Children.Add(grid);DarkTheme.Wrap(this,root);
        entries.SelectionChanged+=(_,_)=>{if(entries.SelectedItem is ListBoxItem {Tag:WorkspaceEntry entry})target.Text=entry.Path;};
        entries.MouseDoubleClick+=async(_,_)=>{if(entries.SelectedItem is ListBoxItem {Tag:WorkspaceEntry entry}){if(entry.IsDirectory)await Navigate(entry.Path);else await Read(false);}};
        Closing+=(_,e)=>{if(changing){e.Cancel=true;status.Text="Finishing the reviewed file update before closing…";return;}closed=true;lifetime.Cancel();};RefreshRoots();
        Loaded+=(_,_)=>{if(roots.Count>0)locations.SelectedItem=roots[0];};
        NameControl(connectPath,"Connect local or network folder");NameControl(address,"Workspace folder path");NameControl(query,"Search file names");NameControl(target,"File action path");NameControl(destination,"File action destination");NameControl(editor,"Workspace file draft");
    }
    private static void NameControl(DependencyObject control,string name)=>System.Windows.Automation.AutomationProperties.SetName(control,name);
    private void Add(Panel row,string label,Func<Task> action)
    {var b=new Button{Content=label,Margin=new Thickness(0,3,6,3),Padding=new Thickness(8,5,8,5)};NameControl(b,label);controls.Add(b);if(row is DockPanel)DockPanel.SetDock(b,Dock.Right);row.Children.Add(b);b.Click+=async(_,_)=>{try{if(!busy&&!changing)await action();}catch(Exception e){status.Text=e.Message;}};}
    private void AddFolderButton(DockPanel row,TextBox target)
    {
        var button=FolderPicker.Button(this,target,"Choose connection folder");button.Margin=new Thickness(0,3,6,3);button.Padding=new Thickness(8,5,8,5);DockPanel.SetDock(button,Dock.Right);controls.Add(button);row.Children.Add(button);
    }
    private void RefreshRoots(){locations.ItemsSource=null;locations.ItemsSource=roots.ToArray();}
    private async Task Run(Func<Task> operation)
    {
        if(busy||changing||inFlight is {IsCompleted:false}){status.Text="A Windows request is still finishing. Wait before retrying.";return;}
        busy=true;foreach(var b in controls)b.IsEnabled=false;locations.IsEnabled=false;status.Text="Reading files…";
        try{inFlight=operation();await inFlight.WaitAsync(TimeSpan.FromSeconds(15),lifetime.Token);}
        catch(TimeoutException){status.Text="The drive or share is slow or unavailable. Windows is still finishing its request; retry after it returns.";}
        catch(Exception e){if(!closed)status.Text=e is OperationCanceledException?"File request cancelled.":e.Message;}
        finally{busy=false;if(!closed){foreach(var b in controls)b.IsEnabled=true;locations.IsEnabled=true;}}
    }
    private async Task Connect()=>await Run(async()=>
    {
        var path=Path.GetFullPath(connectPath.Text);var check=new FileWorkspace([path]);var rows=await Task.Run(()=>check.List(path,lifetime.Token));
        if(!roots.Contains(path,StringComparer.OrdinalIgnoreCase)){if(roots.Count>=16)throw new IOException("Disconnect a folder before adding another (16 maximum).");roots.Add(path);rootsChanged();RefreshRoots();}
        locations.SelectedItem=path;current=path;address.Text=path;ShowEntries(rows);status.Text="Folder connected. Nothing is automatically included in model context.";
    });
    private async Task Navigate(string path)=>await Run(async()=>{var rows=await Task.Run(()=>Workspace.List(path,lifetime.Token));if(closed)return;current=Workspace.Resolve(path);address.Text=current;ShowEntries(rows);status.Text=$"{rows.Count} entries. Select a file to preview or propose an action.";});
    private void ShowEntries(IReadOnlyList<WorkspaceEntry> rows)
    {if(closed)return;entries.Items.Clear();foreach(var entry in rows){var item=new ListBoxItem{Tag=entry,ToolTip=entry.Path,Content=UiIcons.Label((entry.IsDirectory?"":" ")+Path.GetFileName(entry.Path)+(entry.Bytes is long size?$"  ({size:N0} bytes)":""),entry.IsDirectory?"folder":"file")};NameControl(item,(entry.IsDirectory?"Folder: ":"File: ")+entry.Path);entries.Items.Add(item);}}
    private async Task Read(bool add)=>await Run(async()=>{var path=target.Text;var file=await Task.Run(()=>Workspace.Read(path,lifetime.Token));if(closed)return;editor.Text=file.Content;if(add)include(file);status.Text=add?"Screened file included in this chat. Cloud requests still require context review.":"Preview loaded as an editable draft. Review write before saving.";});
    private async Task Review(FileAction action)
    {
        FileActionReceipt? receipt=null;changing=true;locations.IsEnabled=false;entries.IsEnabled=false;foreach(var control in controls)control.IsEnabled=false;
        try{var dialog=new FileActionReviewWindow(Workspace,action){Owner=this};if(ReviewTestAction!=null)dialog.Loaded+=(_,_)=>ReviewTestAction(dialog);if(dialog.ShowDialog()!=true||dialog.Receipt==null)return;receipt=dialog.Receipt;await changed(receipt);}
        finally{changing=false;locations.IsEnabled=true;entries.IsEnabled=true;foreach(var control in controls)control.IsEnabled=true;}
        if(current.Length>0)await Navigate(current);
        status.Text="Applied: "+receipt.Path+(receipt.RecoveryPath==null?"":"\nRecovery: "+receipt.RecoveryPath);
    }
    internal async Task TestConnect(string path){connectPath.Text=path;await Connect();}
    internal bool HasEntries=>entries.Items.Count>0;
    internal Task TestReviewAction(FileAction action)=>Review(action);
}
