using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class CodeSaveWindow : Window
{
    private readonly TextBox folder=new(){Text=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
    private readonly TextBox name=new();
    private readonly TextBox before=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBox after=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button save=new(){Content="Review destination first",IsEnabled=false,Margin=new Thickness(0,8,0,0)};
    private readonly Button preview=new(){Content="Preview exact destination and changes",Margin=new Thickness(0,8,0,12)};
    private CodeSavePlan? plan;
    public CodeSaveReceipt? Receipt {get;private set;}
    internal CodeSavePlan? ReviewedPlan=>plan;
    public static string FileName(string language)=>"Generated"+(language.Trim().ToLowerInvariant() switch{"csharp" or "cs"=>".cs","razor" or "cshtml"=>".cshtml","javascript" or "js"=>".js","typescript" or "ts"=>".ts","sql"=>".sql","python" or "py"=>".py","html"=>".html","css"=>".css","xaml"=>".xaml","json"=>".json",_=>".txt"});
    public CodeSaveWindow(string code,string language)
    {
        Title="Review code and save to file";Width=1050;Height=700;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);name.Text=FileName(language);after.Text=code;
        foreach(var (box,label) in new[]{(folder,"Save destination folder"),(name,"Save file name"),(before,"Current file contents"),(after,"Proposed file contents")})System.Windows.Automation.AutomationProperties.SetName(box,label);
        var root=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Destination folder and file name — nothing is written until you review and confirm.",Margin=new Thickness(0,0,0,8)});
        var browseRow=new DockPanel();var browse=FolderPicker.Button(this,folder);DockPanel.SetDock(browse,Dock.Right);browseRow.Children.Add(browse);browseRow.Children.Add(folder);top.Children.Add(browseRow);top.Children.Add(name);
        top.Children.Add(preview);
        folder.TextChanged+=(_,_)=>Invalidate();name.TextChanged+=(_,_)=>Invalidate();
        preview.Click+=(_,_)=>{try{plan=ReviewedCodeSave.Preview(folder.Text,name.Text,code);before.Text=plan.OriginalContent??"(New file — no existing content)";status.Text=$"{plan.TargetPath}\n{(plan.Overwrites?"Existing file: "+plan.OriginalHash+". A backup will be preserved before overwrite.":"New file. Existing files will never be silently overwritten.")}";save.Content=plan.Overwrites?"Confirm overwrite with reviewed code":"Save reviewed code as new file";save.IsEnabled=true;}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){Invalidate();status.Text=e.Message;}};
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(status);bottom.Children.Add(save);
        save.Click+=(_,_)=>{if(plan==null)return;try{Receipt=ReviewedCodeSave.Apply(plan,plan.Overwrites);DialogResult=true;}catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){Invalidate();status.Text=e.Message;}};
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel{Margin=new Thickness(0,0,6,8)};var right=new DockPanel{Margin=new Thickness(6,0,0,8)};
        var oldLabel=new TextBlock{Text="BEFORE",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(oldLabel,Dock.Top);left.Children.Add(oldLabel);left.Children.Add(before);
        var newLabel=new TextBlock{Text="REVIEWED CODE",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(newLabel,Dock.Top);right.Children.Add(newLabel);right.Children.Add(after);Grid.SetColumn(right,1);grid.Children.Add(left);grid.Children.Add(right);root.Children.Add(grid);DarkTheme.Wrap(this,root);
    }
    private void Invalidate(){plan=null;save.IsEnabled=false;save.Content="Review destination first";}
    internal void TestPreview(string directory,string file){folder.Text=directory;name.Text=file;preview.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    internal void TestSave()=>save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
}
