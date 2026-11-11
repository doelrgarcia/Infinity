using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal sealed class CliHandoffWindow : Window
{
    private readonly ComboBox providers=new();
    private readonly Button send=new(){Content="Send reviewed handoff",Margin=new Thickness(0,12,0,0)};
    private readonly StackPanel transcript=new();
    public ModelProfile? SelectedProvider=>providers.SelectedItem as ModelProfile;
    public CliHandoffPacket Packet {get;}
    internal void TestConfirm()=>send.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal int TestMessageCardCount=>transcript.Children.OfType<Border>().Count(card=>Equals(card.Tag,"HandoffMessageCard"));
    internal Expander TestRequestDetails {get;private set;}=null!;
    public CliHandoffWindow(IEnumerable<ModelProfile> profiles,string preferred,IReadOnlyList<Message> messages,string reason,IReadOnlyList<string>? fileRoots=null)
    {
        Packet=CliEscalation.Prepare(messages,reason,fileRoots);
        Title="Review CLI handoff";Width=900;Height=650;MinWidth=650;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var root=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=reason,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var fileAware=Packet.Messages[0].Content.Contains(FileActionProtocol.Language,StringComparison.OrdinalIgnoreCase);
        top.Children.Add(new TextBlock{Text="Keep this request local, or review it and send it to the selected provider."+(ChatFileTools.Enabled(messages)?" Infinity can automatically read and write screened files in connected folders; overwrites keep backups and other operations require review.":" File changes require review."+(fileAware?" Connected-folder proposals are reviewed separately.":"")),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});
        providers.ItemsSource=profiles.Where(p=>p.IsCloud).ToArray();providers.SelectedItem=providers.Items.Cast<ModelProfile>().FirstOrDefault(p=>p.Id==preferred)??providers.Items.Cast<ModelProfile>().FirstOrDefault();AutomationProperties.SetName(providers,"CLI handoff provider");top.Children.Add(providers);
        var packetSize=new TextBlock{Text=$"{Packet.Prompt.Length:N0} / {CliAssistant.MaximumPromptCharacters:N0} characters to send · {Packet.OmittedCharacters:N0} content characters and {Packet.OmittedMessages} older messages omitted. Excerpts keep their beginning and end; review for missing context.",TextWrapping=TextWrapping.Wrap,Foreground=Paint("#A7B8D4"),Margin=new Thickness(0,10,0,10)};
        if(Packet.OmittedCharacters>0||Packet.OmittedMessages>0)top.Children.Add(new TextBlock{Text="Some content is omitted. Review request details before sending.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);var availability=new TextBlock{TextWrapping=TextWrapping.Wrap};bottom.Children.Add(availability);var actions=new DockPanel();bottom.Children.Add(actions);var keepLocal=new Button{Content="Keep local",IsCancel=true,Margin=new Thickness(0,12,8,0)};keepLocal.Click+=(_,_)=>DialogResult=false;DockPanel.SetDock(keepLocal,Dock.Left);actions.Children.Add(keepLocal);actions.Children.Add(send);
        void Refresh(){var p=SelectedProvider;send.IsEnabled=(ImageInput.All(Packet.Messages).Length==0||p?.Engine=="codex")&&p!=null&&File.Exists(p.Executable)&&(p.Engine!="cursor"||File.Exists(p.ModelPath));availability.Text=send.IsEnabled?p!.Engine=="cursor"?"Cursor on Windows has no OS sandbox. Ask mode and deny hooks restrict tools. Review bridge workspace trust in its interactive CLI yourself; Infinity never auto-approves trust. Cancel to keep local.":"Cancel or close to keep the request local.":"This CLI installation is unavailable. Select another provider or check AI apps & CLI.";}
        providers.SelectionChanged+=(_,_)=>Refresh();Refresh();send.Click+=(_,_)=>{if(send.IsEnabled)DialogResult=true;};
        foreach(var message in Packet.Messages)transcript.Children.Add(MessageCard(message));
        AutomationProperties.SetName(transcript,"Reviewed CLI handoff messages");
        var details=new StackPanel();details.Children.Add(packetSize);details.Children.Add(transcript);
        TestRequestDetails=new Expander{Header="Review request details",IsExpanded=false,Content=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Margin=new Thickness(0,12,0,0)};
        root.Children.Add(TestRequestDetails);DarkTheme.Wrap(this,root);
    }
    private static Border MessageCard(Message message)
    {
        var isUser=message.Role=="user";var isSystem=message.Role=="system";
        var card=new Border{Tag="HandoffMessageCard",Background=Paint(isSystem?"#12161D":isUser?"#303030":"#171B22"),BorderBrush=Paint(isSystem?"#222A35":isUser?"#3A3A3A":"#2B3340"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(isUser?16:12),Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,0,0,10),HorizontalAlignment=isUser?HorizontalAlignment.Right:HorizontalAlignment.Stretch,MaxWidth=isUser?650:double.PositiveInfinity};
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text=isSystem?"System rules":isUser?"You":"Infinity",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Paint(isSystem?"#7F8A9D":isUser?"#98A4B7":"#83A3FF"),Margin=new Thickness(0,0,0,7)});
        foreach(var image in message.Images??[])body.Children.Add(new Image{Source=MainWindow.ImagePreview(image),MaxWidth=500,MaxHeight=200,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8),ToolTip=image.Name});
        var text=MessageBody(message);if(text.Length>0)body.Children.Add(new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Foreground=Paint(isSystem?"#C7D2E5":"#E6EBF5"),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        card.Child=body;AutomationProperties.SetName(card,isSystem?"Reviewed system rules":isUser?"Reviewed user message":"Reviewed assistant response");return card;
    }
    private static string MessageBody(Message message)
    {
        var prompt=CliAssistant.FormatPrompt([message]);var header=message.Role.ToUpperInvariant()+":\n";
        return prompt.StartsWith(header,StringComparison.Ordinal)?prompt[header.Length..]:prompt;
    }
    private static SolidColorBrush Paint(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
