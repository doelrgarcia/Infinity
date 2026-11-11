using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LocalAI.Desktop;

// Native Windows Explorer folder picker shared by every folder field.
internal static class FolderPicker
{
    internal static Func<string?,string?>? TestSelection { get; set; }

    internal static string? Choose(Window? owner,string? initial=null)
    {
        if(TestSelection is { } selection)return selection(initial);
        var dialog=new OpenFolderDialog{Title="Select folder",Multiselect=false};
        var start=StartFolder(initial);
        if(start!=null)dialog.InitialDirectory=start;
        var chosen=owner==null?dialog.ShowDialog():dialog.ShowDialog(owner);
        return chosen==true&&!string.IsNullOrWhiteSpace(dialog.FolderName)?dialog.FolderName:null;
    }

    internal static Button Button(Window? owner,TextBox target,string accessibleName="Choose folder",bool append=false,Action<string>? onChosen=null)
    {
        var button=new Button{Content=UiIcons.Label("Choose folder","folder"),Padding=new Thickness(8,5,8,5),Margin=new Thickness(6,0,0,0),ToolTip="Choose a folder in Windows Explorer",VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(button,accessibleName);
        button.Click+=(_,_)=>
        {
            try
            {
                var chosen=Choose(owner,target.Text);
                if(chosen==null)return;
                if(append)
                {
                    var current=target.Text.TrimEnd();
                    target.Text=(current.Length==0?chosen:current+Environment.NewLine+chosen);
                    target.CaretIndex=target.Text.Length;
                }
                else target.Text=chosen;
                onChosen?.Invoke(chosen);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){MessageBox.Show(owner,e.Message,"Folder picker",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
        return button;
    }

    internal static DockPanel Row(Window? owner,TextBox target,string accessibleName="Choose folder",bool append=false)
    {
        var row=new DockPanel();
        var button=Button(owner,target,accessibleName,append);
        DockPanel.SetDock(button,Dock.Right);
        row.Children.Add(button);
        row.Children.Add(target);
        return row;
    }

    private static string? StartFolder(string? path)
    {
        var first=path?.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).FirstOrDefault();
        if(!string.IsNullOrWhiteSpace(first))
        {
            try
            {
                if(Directory.Exists(first))return first;
                var parent=Path.GetDirectoryName(first);
                if(parent!=null&&Directory.Exists(parent))return parent;
            }
            catch(ArgumentException){}
        }
        return null;
    }
}
