using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly ComboBox cliMode=new(){ItemsSource=new[]{"CLI assistance","Local only"},MinWidth=145};
    private readonly ComboBox fallbackProvider=new(){MinWidth=200};
    private readonly Button askCli=new(){Content="Ask CLI",ToolTip="Ask an installed CLI for help with this task"};
    private Action<CliHandoffWindow>? handoffTestAction;
    private void BuildCliOptions()
    {
        var row=new WrapPanel{Margin=new Thickness(0,10,0,0)};advancedPanel.Children.Add(row);
        row.Children.Add(new TextBlock{Text="When local needs help",FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});row.Children.Add(cliMode);
        fallbackProvider.ItemsSource=settings.Models.Where(p=>p.IsCloud).ToArray();fallbackProvider.Margin=new Thickness(6,0,0,0);row.Children.Add(fallbackProvider);
        advancedPanel.Children.Add(new TextBlock{Text="Large context, failed or timed-out inference, truncated output, or an explicit request for help opens a reviewed CLI handoff. You can also use Ask CLI. Nothing is sent until you confirm.",FontSize=11,Foreground=Brush("#98A4B7"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
        System.Windows.Automation.AutomationProperties.SetName(cliMode,"Local fallback mode");System.Windows.Automation.AutomationProperties.SetName(fallbackProvider,"Preferred CLI helper");System.Windows.Automation.AutomationProperties.SetName(askCli,"Ask CLI for help");
        cliMode.SelectionChanged+=(_,_)=>{if(syncingUi)return;conversation.AutoCliFallback=cliMode.SelectedIndex==0;briefDirty=true;FlushBrief();};
        fallbackProvider.SelectionChanged+=(_,_)=>{if(syncingUi||fallbackProvider.SelectedItem is not ModelProfile p)return;conversation.FallbackProviderId=p.Id;briefDirty=true;FlushBrief();};
        askCli.Click+=async(_,_)=>await AskCli();idleControls.AddRange([cliMode,fallbackProvider,askCli]);
    }
    private async Task AskCli()
    {
        if(busy)return;var fresh=!string.IsNullOrWhiteSpace(prompt.Text);var text=fresh?prompt.Text:conversation.Messages.LastOrDefault(m=>m.Role=="user")?.Content;
        if(string.IsNullOrWhiteSpace(text)){status.Text="Describe a task first, then choose Ask CLI.";return;}
        await Escalate(text,BuildMessages(text,fresh),"You requested CLI help with this task",fresh);
    }
    private async Task Escalate(string text,IReadOnlyList<Message> messages,string reason,bool appendUser)
    {
        if(busy)return;
        if(!settings.Models.Any(p=>p.IsCloud)){status.Text="CLI help is unavailable. Configure an installed provider in appsettings.json. Your local task is retained.";return;}
        ModelProfile? provider=null;IReadOnlyList<Message>? outgoing=null;
        SetBusy(true);
        try
        {
            var dialog=new CliHandoffWindow(settings.Models,conversation.FallbackProviderId,messages,reason,conversation.WorkspaceRoots.Count>0?conversation.WorkspaceRoots:null){Owner=this};if(handoffTestAction!=null)dialog.Loaded+=(_,_)=>handoffTestAction(dialog);
            if(dialog.ShowDialog()==true&&dialog.SelectedProvider is { } selected){provider=selected;outgoing=dialog.Packet.Messages;conversation.FallbackProviderId=selected.Id;}
            else {status.Text="CLI handoff cancelled before sending. Local results and request retained.";if(string.IsNullOrWhiteSpace(prompt.Text))prompt.Text=text;}
        }
        catch(Exception e){status.Text="Could not prepare CLI handoff: "+e.Message;}
        finally{SetBusy(false);}
        if(provider!=null&&outgoing!=null)await ExecuteRequest(provider,text,outgoing,appendUser,false,true);
    }
}
