using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private bool IsFileActivity(int index)
    {
        var message=conversation.Messages[index];
        if(message.Role=="user")return message.Content.StartsWith("File tool results",StringComparison.Ordinal)||message.Content.StartsWith(ChatCommands.ResultPrefix,StringComparison.Ordinal)||message.Content.StartsWith("Automatic file action receipt",StringComparison.Ordinal);
        return message.Role=="assistant"&&index+1<conversation.Messages.Count&&conversation.Messages[index+1].Role=="user"&&(conversation.Messages[index+1].Content.StartsWith("File tool results",StringComparison.Ordinal)||conversation.Messages[index+1].Content.StartsWith(ChatCommands.ResultPrefix,StringComparison.Ordinal)||conversation.Messages[index+1].Content.StartsWith("Automatic file action receipt",StringComparison.Ordinal));
    }
    private void RenderFileActivity(List<Message> activity)
    {
        if(activity.Count==0)return;
        var details=new TextBox{Text=string.Join("\n\n",activity.Select(m=>m.Content)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brush("#171B22"),Foreground=Foreground,BorderThickness=new Thickness(0),Padding=new Thickness(10),MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        transcript.Children.Add(new Expander{Header="Work details — show",Tag="FileActivityDetails",IsExpanded=false,Content=details,Foreground=Brush("#98A4B7"),Margin=new Thickness(0,8,0,12)});
        activity.Clear();
    }
}
