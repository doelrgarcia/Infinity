using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class DependencySetupWindow : Window
{
    private readonly Settings settings;
    private readonly StartupDependencies setup;
    private readonly StackPanel rows=new();
    private readonly Dictionary<string,CheckBox> selections=[];
    private readonly Dictionary<string,TextBlock> packageStatus=[];
    private readonly TextBlock heading=new(){FontSize=24,FontWeight=FontWeights.SemiBold,Text="Checking tools and models…"};
    private readonly TextBox status=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=140,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0,10,0,8)};
    private readonly ProgressBar progress=new(){Height=8,Minimum=0,Maximum=100,Margin=new Thickness(0,0,0,12)};
    private readonly Button install=new(){Content="Install selected",Margin=new Thickness(0,0,8,0)};
    private readonly Button continueApp=new(){Content="Continue with available tools",IsEnabled=false,Margin=new Thickness(0,0,8,0)};
    private readonly Button stop=new(){Content="Cancel download",IsEnabled=false};
    private readonly CancellationTokenSource closing=new();
    private CancellationTokenSource? active;
    private bool busy;
    private bool closeRequested;
    private bool repairMode;
    private DependencyInspection? inspection;
    internal Task? TestTask {get;private set;}
    internal void TestSelect(params string[] ids){foreach(var pair in selections)pair.Value.IsChecked=ids.Contains(pair.Key);}
    internal Task TestInstall(){install.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));return TestTask??Task.CompletedTask;}
    internal bool TestContinueEnabled=>continueApp.IsEnabled;
    internal bool TestBusy=>busy;
    internal int TestSelections=>selections.Values.Count(c=>c.IsChecked==true);
    internal string TestStatus=>status.Text;
    internal Task TestCheck()=>Check(false);
    internal void TestCancel()=>active?.Cancel();
    public DependencySetupWindow(Settings settings,StartupDependencies setup,bool enableRepair=false)
    {
        this.settings=settings;this.setup=setup;repairMode=enableRepair;Title="Infinity · Startup setup";Width=900;Height=690;MinWidth=650;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterScreen;DarkTheme.Apply(this);
        var root=new DockPanel{Margin=new Thickness(22)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(heading);
        top.Children.Add(new TextBlock{Text="Choose which missing dependencies to install. Downloads use pinned publisher packages and verified hashes. Existing installations, models and appsettings.json are preserved. Account sign-in and hardware drivers remain separate.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});
        var topActions=new WrapPanel();var all=new Button{Content="Select all missing",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,8,10)};all.Click+=(_,_)=>{if(!busy)foreach(var c in selections.Values)c.IsChecked=true;};topActions.Children.Add(all);var recheck=new Button{Content="Recheck",Margin=new Thickness(0,0,0,10)};recheck.Click+=async(_,_)=>{if(!busy)await Check(false);};topActions.Children.Add(recheck);top.Children.Add(topActions);
        var repair=new Button{Content="Repair managed downloads",Margin=new Thickness(8,0,0,10)};repair.Click+=(_,_)=>{if(busy)return;repairMode=!repairMode;repair.Content=repairMode?"Show missing only":"Repair managed downloads";all.Content=repairMode?"Select all listed":"Select all missing";Refresh();};topActions.Children.Add(repair);
        if(repairMode){repair.Content="Show missing only";all.Content="Select all listed";}
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(status);bottom.Children.Add(progress);var actions=new WrapPanel();actions.Children.Add(install);actions.Children.Add(continueApp);actions.Children.Add(stop);bottom.Children.Add(actions);
        install.Click+=async(_,_)=>{TestTask=InstallSelected();await TestTask;};continueApp.Click+=(_,_)=>DialogResult=true;stop.Click+=(_,_)=>active?.Cancel();root.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});DarkTheme.Wrap(this,root);
        System.Windows.Automation.AutomationProperties.SetName(progress,"Dependency download progress");System.Windows.Automation.AutomationProperties.SetName(status,"Startup setup status");
        Loaded+=async(_,_)=>await Check(!repairMode);
        Closing+=(_,e)=>{if(busy){closeRequested=true;active?.Cancel();e.Cancel=true;status.Text="Cancelling setup. Verified downloads and resumable partial files remain in the managed cache.";}};
        Closed+=(_,_)=>{closing.Cancel();closing.Dispose();};
    }
    private async Task Check(bool closeWhenReady)
    {
        busy=true;active=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);install.IsEnabled=false;continueApp.IsEnabled=false;stop.IsEnabled=true;string? error=null;
        try{status.Text="Checking installed tools. No downloads are started by this check.";await setup.CheckTools(active.Token);}
        catch(OperationCanceledException){error="Startup check cancelled. Recheck when ready.";}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or HttpRequestException){error="Could not inspect dependencies: "+e.Message+". Repair cache access and Recheck.";}
        finally{active.Dispose();active=null;busy=false;stop.IsEnabled=false;}
        if(closeRequested){DialogResult=false;return;}try{Refresh();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=error??e.Message;return;}if(error!=null)status.Text=error;else if(closeWhenReady&&inspection!.Missing.Count==0&&inspection.Notices.Count==0)DialogResult=true;
    }
    private static string Size(long bytes)=>bytes>=1073741824?$"{bytes/1073741824d:F2} GiB":$"{bytes/1048576d:F1} MiB";
    private void Refresh()
    {
        var selected=selections.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToHashSet();inspection=setup.Inspect();rows.Children.Clear();selections.Clear();packageStatus.Clear();
        if(repairMode)inspection=new(setup.Catalog.Packages,inspection.ReadyProfiles,inspection.Notices);
        heading.Text=inspection.Missing.Count==0?"Available tools checked":"Install missing tools and models";
        if(repairMode)heading.Text="Repair managed downloads";
        install.Content=repairMode?"Repair selected":"Install selected";
        foreach(var p in inspection.Missing)
        {
            var panel=new StackPanel{Margin=new Thickness(8,8,8,12)};var checkbox=new CheckBox{Content=$"{p.Label} · {Size(p.DownloadBytes)}",IsChecked=selected.Contains(p.Id)};System.Windows.Automation.AutomationProperties.SetName(checkbox,"Install "+p.Label);panel.Children.Add(checkbox);selections[p.Id]=checkbox;checkbox.Checked+=(_,_)=>SelectionChanged();checkbox.Unchecked+=(_,_)=>SelectionChanged();
            panel.Children.Add(new TextBlock{Text=$"Pinned version {p.Version}\nPublisher/license: {p.LicenseUrl}",FontSize=11,Foreground=MainWindowBrush(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(26,4,0,0)});var detail=new TextBlock{Text="Not installed",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(26,5,0,0)};panel.Children.Add(detail);packageStatus[p.Id]=detail;rows.Children.Add(panel);
            if(p.NativeInstaller!=null)panel.Children.Add(new TextBlock{Text="Installs a Windows system prerequisite. Administrator approval may be requested. Infinity will not restart Windows.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(26,5,0,0)});
            if(repairMode)detail.Text=setup.Installer.IsInstalled(p)?"Cached package · repair downloads and verifies again":"Not cached";
        }
        foreach(var notice in inspection.Notices)rows.Children.Add(new TextBlock{Text=notice,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)});
        continueApp.IsEnabled=!busy&&inspection.ReadyProfiles.Count>0;status.Text=$"{inspection.ReadyProfiles.Count} of {settings.Models.Count} profiles available. {inspection.Missing.Count} missing packages. Nothing downloads until you select Install.";SelectionChanged();
        if(repairMode)status.Text="Select managed packages to download and verify again. Existing cache files are backed up at replacement. External installations and compatible Windows runtimes are preserved. Large models download again.";
    }
    private void SelectionChanged()
    {
        install.IsEnabled=!busy&&selections.Any(p=>p.Value.IsChecked==true);
        if(!busy&&inspection!=null&&selections.Any(p=>p.Value.IsChecked==true))
        {
            var required=setup.RequiredForSelection(inspection.Missing.Where(p=>selections[p.Id].IsChecked==true));foreach(var p in required)if(selections.TryGetValue(p.Id,out var c)&&c.IsChecked!=true)c.IsChecked=true;
            var total=required.Sum(p=>p.DownloadBytes);status.Text=$"Selected download: {Size(total)} including required supporting tools. Large models may take time. Cancel keeps resumable downloads.";
        }
    }
    private async Task InstallSelected()
    {
        if(busy||inspection==null)return;var packages=setup.RequiredForSelection(inspection.Missing.Where(p=>selections[p.Id].IsChecked==true));if(packages.Count==0){status.Text="Select one or more missing dependencies first.";return;}
        busy=true;active=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);install.IsEnabled=false;continueApp.IsEnabled=false;stop.IsEnabled=true;foreach(var c in selections.Values)c.IsEnabled=false;var failures=new List<string>();
        try
        {
            foreach(var p in packages)
            {
                try
                {
                    var updates=new Progress<DependencyProgress>(u=>{if(!busy||active?.IsCancellationRequested!=false)return;progress.Value=u.Total>0?100d*u.Completed/u.Total:0;status.Text=$"{u.Package} · {u.Phase}"+(u.Total>0?$" · {Size(u.Completed)} / {Size(u.Total)}":"");});
                    await setup.InstallPackage(p,updates,active.Token,repairMode);packageStatus[p.Id].Text="Installed; verifying availability…";
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e) when(e is IOException or HttpRequestException or UnauthorizedAccessException or InvalidDataException){failures.Add(p.Label+": "+e.Message);packageStatus[p.Id].Text=e.Message;}
            }
            await setup.CheckTools(active.Token);
        }
        catch(OperationCanceledException){failures.Add("Setup cancelled. Partial downloads are retained for a verified resume.");}
        finally{active.Dispose();active=null;busy=false;stop.IsEnabled=false;if(closeRequested)DialogResult=false;else{Refresh();if(failures.Count>0)status.Text=string.Join("\n",failures)+"\nSelect the missing package and Install to retry. Working tools remain available.";else status.Text="Selected dependencies installed. Review any prerequisite notices above; continue with available tools, or choose more packages.";}}
    }
    private static System.Windows.Media.Brush MainWindowBrush()=>new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(167,184,212));
}
