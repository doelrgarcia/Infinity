using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async void OpenIsolatedWorkspace()=>await CreateIsolatedWorkspace();
    private async Task CreateIsolatedWorkspace()
    {
        try
        {
            RequireProjectNavigationIdle();var project=ProjectForChat()??throw new IOException("Open a project with a Git folder first.");
            if(project.Roots.Count!=1)throw new IOException("Use a project with one Git working-copy root.");
            var dialog=new WorkspaceIsolationWindow{Owner=this};if(dialog.ShowDialog()!=true)return;
            SetBusy(true);active=new();WorkspacePlan plan;
            try{plan=await IsolatedWorkspace.Preview(project.Roots[0],dialog.Revision,Path.Combine(AgentDirectory,"workspaces"),active.Token);}
            finally{active.Dispose();active=null;SetBusy(false);}
            if(!AgentView.Confirm(this,"Create isolated workspace",$"Project: {project.Name}\nRepository: {plan.Repository}\nExact commit: {plan.Commit}\nNew folder: {plan.Destination}\n\nCreate a detached Git worktree and a separate Infinity project? Only committed files are checked out. Uncommitted files and ignored/private settings are not copied. Hooks, checkout filters and submodules are disabled during creation. File and command reviews remain required. Commands can still access this PC; this is not an operating-system sandbox. No merge, push or deletion is performed."))return;
            SetBusy(true);active=new();try{await IsolatedWorkspace.Create(plan,active.Token);}finally{active.Dispose();active=null;SetBusy(false);}
            try{RegisterIsolatedProject(project,plan);}catch(Exception e){throw new IOException("Workspace created at "+plan.Destination+", but its Infinity project could not be saved/opened. Folder retained. "+e.Message,e);}status.Text="Isolated workspace created and opened. Original project and drafts retained.";
        }
        catch(Exception e){status.Text="Isolated workspace: "+e.Message;}
    }
    private ProjectMemory RegisterIsolatedProject(ProjectMemory original,WorkspacePlan plan)
    {
        var name=original.Name[..Math.Min(80,original.Name.Length)]+" · isolated "+Path.GetFileName(plan.Destination)[..8];
        var project=new ProjectMemory{Name=name,Roots=[plan.Destination],Purpose=original.Purpose,Conventions=original.Conventions,BuildCommand=original.BuildCommand,TestCommand=original.TestCommand,IsolationRepository=plan.Repository,IsolationCommit=plan.Commit};
        SaveProject(project,select:false);SelectProjectChats(project.Id);return project;
    }
}
internal sealed class WorkspaceIsolationWindow:Window
{
    private readonly TextBox revision=new(){Text="HEAD",MaxLength=200};
    internal string Revision=>revision.Text.Trim();
    internal WorkspaceIsolationWindow()
    {
        Title="Isolated workspace";Width=520;Height=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);var body=new StackPanel{Margin=new Thickness(24)};
        body.Children.Add(new TextBlock{Text="Work on a separate copy",FontSize=22});body.Children.Add(new TextBlock{Text="Branch or commit",Margin=new Thickness(0,16,0,6)});body.Children.Add(revision);body.Children.Add(new TextBlock{Text="The next screen reviews the exact commit and new folder. Your current files stay in place.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)});
        var actions=new WrapPanel();var preview=new Button{Content="Preview",IsDefault=true};preview.Click+=(_,_)=>DialogResult=true;actions.Children.Add(preview);actions.Children.Add(new Button{Content="Cancel",IsCancel=true});body.Children.Add(actions);DarkTheme.Wrap(this,body);
    }
}
