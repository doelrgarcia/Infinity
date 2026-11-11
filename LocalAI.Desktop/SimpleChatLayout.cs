using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private readonly Button returnToChat=new(){Content="Back to chat",Visibility=Visibility.Collapsed};
    private Button Disclosure(string label,string name,params (string Label,Action Action)[] actions)
    {
        var button=FlatButton(label,()=>{},name);
        var menu=new ContextMenu{Background=Brush("#303030"),Foreground=Brush("#EEEEEE"),BorderBrush=Brush("#4A4A4A"),MinWidth=220};
        var itemTemplate=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
              <Border x:Name="Row" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#484848"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        foreach(var (text,action) in actions)
        {
            var item=new MenuItem{Header=text,Template=itemTemplate,Padding=new Thickness(16,10,16,10),Background=Brush("#303030"),Foreground=Brush("#EEEEEE")};
            item.Click+=(_,_)=>action();
            menu.Items.Add(item);
        }
        button.ContextMenu=menu;
        button.Click+=(_,_)=>{menu.PlacementTarget=button;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
        return button;
    }
    private void OpenChatOptions()
    {
        ShowChat();
        advancedPanel.Visibility=advancedPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
    }
    private void ShowChatFiles(string section)
    {
        ShowChat();
        SelectInspector(section);
        if(inspectorColumn.Width.Value==0)ToggleInspector();
    }
}
