using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class ProviderConsent : Window
{
    private readonly Button send=new(){Content="Send this reviewed context to cloud provider",Margin=new Thickness(0,12,0,0)};
    internal IReadOnlyList<Message> ReviewedMessages {get;}
    internal void TestConfirm()=>send.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public ProviderConsent(ModelProfile profile,IReadOnlyList<Message> messages)
    {
        ReviewedMessages=messages.ToArray();
        Title="Review cloud provider request";Width=850;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var fileNotice=ChatFileTools.Enabled(messages)?"Infinity can automatically read and write screened files inside this chat's connected folders. Overwrites retain backups; other file operations require review.":"The CLI bridge proposes code; Save remains a separate reviewed action.";
        var root=new DockPanel{Margin=new Thickness(18)};var label=new TextBlock{Text=$"Send the text and any attached images below to {profile.Label}? This includes selected source files, research excerpts and this conversation. The provider's account terms and usage limits apply. {fileNotice}{(profile.Engine=="cursor"?" Cursor on Windows has no OS sandbox. Ask mode and deny hooks restrict tools; review workspace trust yourself in its interactive CLI. This is a text-only request to a cloud provider.":"")}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(label,Dock.Top);root.Children.Add(label);
        DockPanel.SetDock(send,Dock.Bottom);root.Children.Add(send);send.Click+=(_,_)=>DialogResult=true;
        var review=new StackPanel();foreach(var image in ImageInput.All(messages)){review.Children.Add(new Image{Source=MainWindow.ImagePreview(image),MaxHeight=200,MaxWidth=500});review.Children.Add(new TextBlock{Text=image.Name+" · SHA256 "+image.Sha256,TextWrapping=TextWrapping.Wrap});}review.Children.Add(new TextBox{Text=CliAssistant.FormatPrompt(messages),IsReadOnly=true,TextWrapping=TextWrapping.Wrap});root.Children.Add(new ScrollViewer{Content=review,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});DarkTheme.Wrap(this,root);
    }
}
