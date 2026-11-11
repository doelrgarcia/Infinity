using System.Diagnostics;

using System.IO;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using LocalAI.Core;



namespace LocalAI.Desktop;

internal sealed class AiToolsWindow : Window

{

    private readonly List<(string Name,Button Button)> launchButtons=[];

    private readonly Dictionary<string,TextBlock> availability=[];
    private readonly Dictionary<string,Button> providerButtons=[];
    internal void TestUseProvider(string engine)=>providerButtons[engine].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    internal Dictionary<string,bool> LaunchResults {get;}=[];

    internal void TestLaunchButtons(string? provider=null,string? action=null){foreach(var (name,button) in launchButtons.Where(p=>provider==null||p.Name.StartsWith(provider+" / ",StringComparison.Ordinal)).Where(p=>action==null||p.Name.EndsWith(" / "+action,StringComparison.Ordinal)))button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}

    private static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    public AiToolsWindow(Settings settings,Action<ProcessStartInfo>? start=null,Func<string?>? deepSeekCli=null,Action<ModelProfile>? useProvider=null)

    {

        start??=info=>Process.Start(info);

        Title="AI apps & CLI";Width=880;Height=760;MinWidth=650;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);

        var shell=new DockPanel{Margin=new Thickness(26,12,26,22)};

        var heading=new StackPanel{Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);

        heading.Children.Add(new TextBlock{Text="AI apps & CLI",FontSize=24,FontWeight=FontWeights.SemiBold});

        heading.Children.Add(new TextBlock{Text="Open an assistant or manage your tools.",Foreground=Brush("#98A4B7"),FontSize=14,Margin=new Thickness(0,6,0,0)});

        var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);

        var status=new TextBlock{Text="Launch buttons open the assistant without sending your Infinity prompt or files.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#98A4B7"),Margin=new Thickness(0,10,0,0)};

        var setupButton=new Button{Content="Check tools & models",HorizontalAlignment=HorizontalAlignment.Left};

        setupButton.Click+=(_,_)=>{using var dependencies=new StartupDependencies(settings);new DependencySetupWindow(settings,dependencies){Owner=this}.ShowDialog();RefreshAvailability();status.Text="Tools checked. Choose an assistant above, or return to your conversation.";};footer.Children.Add(setupButton);footer.Children.Add(status);

        var repairButton=new Button{Content="Repair managed downloads",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};repairButton.Click+=(_,_)=>{using var dependencies=new StartupDependencies(settings);new DependencySetupWindow(settings,dependencies,true){Owner=this}.ShowDialog();RefreshAvailability();status.Text="Repair closed. Check the selected tools before continuing.";};footer.Children.Insert(1,repairButton);

        var rows=new StackPanel();shell.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});

        foreach(var (kind,label) in new[]{("cursor","Cursor"),("grok","Grok"),("codex","ChatGPT / Codex"),("deepseek","DeepSeek Harness"),("copilot","GitHub Copilot")})

        {

            var body=new StackPanel{Margin=new Thickness(0,16,0,16)};

            rows.Children.Add(new Border{BorderBrush=Brush("#303844"),BorderThickness=new Thickness(0,1,0,0),Child=body});

            body.Children.Add(new TextBlock{Text=label,FontSize=17,FontWeight=FontWeights.SemiBold});

            var found=new TextBlock{FontSize=12,Margin=new Thickness(0,5,0,12)};availability.Add(kind,found);body.Children.Add(found);

            var actions=new WrapPanel();body.Children.Add(actions);
            var connectedProfile=settings.Models.FirstOrDefault(p=>p.Engine==kind);
            var use=new Button{Content="Use in Infinity",Margin=new Thickness(0,0,8,5),Padding=new Thickness(14,8,14,8),IsEnabled=useProvider!=null&&connectedProfile!=null&&File.Exists(connectedProfile.Executable)&&(kind!="cursor"||File.Exists(connectedProfile.ModelPath))};
            System.Windows.Automation.AutomationProperties.SetName(use,label+": Use in Infinity");providerButtons.Add(kind,use);actions.Children.Add(use);
            use.Click+=(_,_)=>{if(!use.IsEnabled||connectedProfile==null)return;useProvider!(connectedProfile);Close();};

            foreach(var action in kind=="copilot"?new[]{"Open website","Open interactive CLI"}:new[]{"Open desktop","Open website","Open interactive CLI"})

            {

                var text=action;

                if(action=="Open desktop"&&AiLaunchers.DesktopPath(kind)==null)text=kind=="deepseek"?"Install desktop / CLI":"Open desktop / website";

                var button=new Button{Content=text,Margin=new Thickness(0,0,8,5),Padding=new Thickness(14,8,14,8)};

                if(action=="Open desktop"){button.Background=Brush("#4267D5");button.BorderBrush=Brush("#577FE6");}

                System.Windows.Automation.AutomationProperties.SetName(button,label+": "+text);

                actions.Children.Add(button);var name=kind+" / "+action;launchButtons.Add((name,button));

                button.Click+=(_,_)=>

                {

                    try

                    {

                        var profile=settings.Models.FirstOrDefault(m=>m.Engine==kind);

                        var info=action switch{"Open desktop"=>AiLaunchers.DesktopStart(kind),"Open website"=>AiLaunchers.WebsiteStart(kind),_=>AiLaunchers.CliStart(kind,Path.Combine(AppContext.BaseDirectory,"launch-provider.ps1"),profile,deepSeekCli)};

                        start(info);LaunchResults[name]=true;

                        status.Text=$"{label}: launch requested. Review sign-in and permissions in its interface. No Infinity prompt or files were sent.";

                    }

                    catch(Exception e){LaunchResults[name]=false;status.Text="Cannot launch: "+e.Message;}

                };

            }

            if(kind=="copilot")body.Children.Add(new TextBlock{Text="Use your GitHub Copilot CLI sign-in. Requests are reviewed before sending; Infinity handles connected-folder files and reviewed commands. Sign in through Open interactive CLI if needed.",Foreground=Brush("#98A4B7"),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,4,0,0)});
            if(kind=="deepseek")body.Children.Add(new TextBlock{Text="Select DeepSeek in Infinity to use the installed Harness sign-in. Each cloud request is reviewed. The Infinity bridge returns text only; file changes and commands stay in Infinity’s review controls.",Foreground=Brush("#98A4B7"),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,4,0,0)});

        }

        void RefreshAvailability()

        {

            foreach(var (kind,view) in availability)

            {

                var profile=settings.Models.FirstOrDefault(p=>p.Engine==kind);

                var found=kind=="deepseek"?AiLaunchers.DeepSeekCliPath()!=null:profile!=null&&File.Exists(profile.Executable)&&(kind!="cursor"||File.Exists(profile.ModelPath));

                view.Text=found?kind=="deepseek"?"Harness found · Infinity chat with context review":"CLI found · Infinity chat with context review":"CLI not found — check tools or install the provider";

                view.Text+=" · "+(AiLaunchers.DesktopPath(kind)!=null?"desktop detected":"desktop unavailable; website fallback");

                view.Foreground=Brush(found?"#60C8BA":"#DAB575");

            }

        }

        RefreshAvailability();DarkTheme.Wrap(this,shell);

    }

}
