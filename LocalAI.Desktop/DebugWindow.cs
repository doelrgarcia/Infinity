using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal sealed class DebugWindow : Window
{
    private readonly string[] roots;
    private readonly TextBox project=new(){Padding=new Thickness(8)},packet=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ComboBox operation=new(){ItemsSource=new[]{"Build","Test","Run"},SelectedIndex=0,Width=100};
    private readonly TextBlock status=new(){Text="Select a project, run a reviewed command, then ask Infinity to diagnose the result.",TextWrapping=TextWrapping.Wrap};
    private readonly Button diagnose=new(){Content="Ask Infinity to diagnose",IsEnabled=false};
    internal string? DiagnosisPrompt {get;private set;}
    internal CommandRun? Result {get;private set;}
    internal Func<CommandWindow,Task>? RunTestAction {get;set;}
    internal string ProjectPath=>project.Text;
    internal DebugWindow(IEnumerable<string> roots,string? logRoot=null,CommandRun? previous=null,string? projectPath=null)
    {
        this.roots=roots.ToArray();Title="Infinity • Debug code";Width=Math.Min(980,SystemParameters.WorkArea.Width-24);Height=Math.Min(720,SystemParameters.WorkArea.Height-24);WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var body=new DockPanel{Margin=new Thickness(18)};DarkTheme.Wrap(this,body);var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);body.Children.Add(top);top.Children.Add(new TextBlock{Text="Run → diagnose → review a fix → rerun",FontSize=22,Margin=new Thickness(0,0,0,12)});top.Children.Add(new TextBlock{Text="Project file (.csproj, .sln, package.json or JavaScript file)"});top.Children.Add(project);
        var actions=new WrapPanel{Margin=new Thickness(0,10,0,10)};top.Children.Add(actions);actions.Children.Add(operation);Button run=new Button{Content="Review and run"},rerun=new Button{Content="Rerun last command",IsEnabled=false},detect=new Button{Content="Find project in folder"};actions.Children.Add(detect);actions.Children.Add(run);actions.Children.Add(rerun);actions.Children.Add(diagnose);
        top.Children.Add(new TextBlock{Text="Builds/tests can execute project scripts and contact package sources. Each run uses the existing exact-command review. Commands can affect files beyond the starting folder; fixes still require file review. Run servers stop when their time limit ends or you press Stop.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        DockPanel.SetDock(status,Dock.Bottom);body.Children.Add(status);body.Children.Add(packet);System.Windows.Automation.AutomationProperties.SetName(project,"Debug project path");System.Windows.Automation.AutomationProperties.SetName(packet,"Debug diagnosis context");
        CommandRequest? last=previous?.Request;if(previous!=null){Result=previous;packet.Text=DebugWorkflow.Diagnosis(previous);diagnose.IsEnabled=true;rerun.IsEnabled=true;}
        void Execute(CommandRequest request){var window=new CommandWindow(this.roots,request,logRoot:logRoot){Owner=this};if(RunTestAction!=null)window.Loaded+=async(_,_)=>await RunTestAction(window);window.ShowDialog();if(window.Result is not { } result)return;Result=result;last=result.Request;packet.Text=DebugWorkflow.Diagnosis(result);diagnose.IsEnabled=true;rerun.IsEnabled=true;status.Text=$"{result.Status} • exit {result.ExitCode}. Review the exact context before asking Infinity; output may contain secrets.";}
        run.Click+=(_,_)=>{try{Execute(DebugWorkflow.Plan(this.roots,project.Text,operation.SelectedItem as string??"Build"));}catch(Exception e){status.Text=e.Message;}};
        rerun.Click+=(_,_)=>{if(last!=null)try{Execute(last);}catch(Exception e){status.Text=e.Message;}};
        diagnose.Click+=(_,_)=>{if(Result==null)return;DiagnosisPrompt=packet.Text;Close();};
        detect.Click+=(_,_)=>Detect();Detect();if(projectPath!=null)project.Text=projectPath;
    }
    private void Detect(){try{var workspace=new FileWorkspace(roots);var projects=roots.SelectMany(root=>workspace.List(root)).Where(e=>!e.IsDirectory&&(Path.GetExtension(e.Path) is ".csproj" or ".sln" or ".slnx"||Path.GetFileName(e.Path)=="package.json")).ToArray();if(projects.Length==1)project.Text=projects[0].Path;else status.Text=projects.Length==0?"Paste a project file path inside a connected folder.":"Several projects found. Paste the one you want to debug:\n"+string.Join("\n",projects.Take(10).Select(e=>e.Path));}catch(Exception e){status.Text=e.Message;}}
    internal void TestProject(string value)=>project.Text=value;
    internal bool TestDiagnosisDisabled=>!diagnose.IsEnabled&&DiagnosisPrompt==null;
}
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid,(string Project,CommandRun Result)> debugSessions=[];
    private async Task OpenDebug()
    {
        if(busy||dictation!=null)return;if(conversation.WorkspaceRoots.Count==0)OpenFileWorkspace();if(conversation.WorkspaceRoots.Count==0){status.Text="Connect a project folder to debug its code.";return;}
        var owner=conversation.Id;debugSessions.TryGetValue(owner,out var previous);var window=new DebugWindow(conversation.WorkspaceRoots,previous:previous.Result,projectPath:previous.Project){Owner=this};window.ShowDialog();if(window.Result!=null)debugSessions[owner]=(window.ProjectPath,window.Result);if(window.DiagnosisPrompt is { } diagnosis){prompt.Text=diagnosis;await Send();}
    }
}
