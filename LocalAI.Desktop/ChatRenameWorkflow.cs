using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private Action<RenameChatWindow>? renameChatTest;
    private ContextMenu ChatMenu(Conversation chat)=>Disclosure("","",("Rename chat",()=>RenameChat(chat)),("Delete chat",()=>DeleteChat(chat))).ContextMenu;
    private void RenameChat(Conversation chat)
    {
        try
        {
            RequireProjectNavigationIdle();if(IsChatDeleted(chat))throw new IOException("Restore this chat before renaming it.");
            var dialog=new RenameChatWindow(chat.Title,title=>
            {
                var previous=chat.Title;chat.Title=title;
                try{store.Save(chat);}catch{chat.Title=previous;throw;}
            }){Owner=this};
            if(renameChatTest!=null)dialog.Loaded+=(_,_)=>renameChatTest(dialog);
            if(dialog.ShowDialog()!=true)return;
            history.Items.Refresh();RefreshProjectNavigation();if(conversation.Id==chat.Id)Render();
            status.Text="Chat renamed: "+chat.Title;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){status.Text="Could not rename chat: "+e.Message;}
    }
}

internal sealed class RenameChatWindow:Window
{
    private readonly TextBox title=new(){MaxLength=160};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly Button rename=new(){Content="Rename",IsDefault=true};
    internal RenameChatWindow(string current,Action<string> save)
    {
        Title="Rename chat";Width=460;Height=280;MinWidth=360;MinHeight=240;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var body=new DockPanel{Margin=new Thickness(22)};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
        actions.Children.Add(new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)});actions.Children.Add(rename);
        var form=new StackPanel();body.Children.Add(form);form.Children.Add(new TextBlock{Text="Chat name",FontSize=18,Margin=new Thickness(0,0,0,12)});title.Text=current;System.Windows.Automation.AutomationProperties.SetName(title,"Chat name");form.Children.Add(title);form.Children.Add(error);DarkTheme.Wrap(this,body);
        rename.Click+=(_,_)=>
        {
            try{var value=title.Text.Trim();AppControlProtocol.Validate(new("renameChat",value));save(value);DialogResult=true;}
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){error.Text="Could not rename chat: "+e.Message;}
        };
        Loaded+=(_,_)=>{title.Focus();title.SelectAll();};
    }
    internal void TestSubmit(string value){title.Text=value;rename.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    internal string TestError=>error.Text;
}
