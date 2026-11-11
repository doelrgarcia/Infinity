using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal sealed class FolderBatchReviewWindow:Window
{
    private readonly FileWorkspace workspace;
    private readonly IReadOnlyList<FileAction> actions;
    private readonly TextBox details=FileActionReviewWindow.TextArea();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    private readonly Button preview=new(){Content="Preview all folders"},apply=new(){Content="Create reviewed folders",IsEnabled=false},individual=new(){Content="Review one at a time"};
    private List<FileActionPlan>? plans;
    private bool busy;
    internal Action<int,FileActionPlan>? BeforeApply;
    internal Action<int,FileActionReceipt>? AfterApply;
    internal List<FileActionReceipt> Receipts {get;}=[];
    internal bool ReviewIndividually {get;private set;}
    internal string? Failure {get;private set;}
    internal FolderBatchReviewWindow(FileWorkspace workspace,IReadOnlyList<FileAction> actions)
    {
        if(actions.Count<2||actions.Any(a=>a.Operation!="mkdir"||a.Destination!=null||a.Content!=null))throw new IOException("Grouped review supports folder creation only.");
        this.workspace=workspace;this.actions=actions;Title="Review folder group";Width=850;Height=580;MinWidth=500;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var body=new DockPanel{Margin=new Thickness(18)};DarkTheme.Wrap(this,body);var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);
        header.Children.Add(new TextBlock{Text=$"Create {actions.Count} folders",FontSize=24});header.Children.Add(new TextBlock{Text="Preview every full path, then create this exact group. No file contents are changed. Each folder is checked again before creation; any partial outcome is recorded.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});header.Children.Add(preview);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);footer.Children.Add(status);var buttons=new WrapPanel();footer.Children.Add(buttons);buttons.Children.Add(apply);buttons.Children.Add(individual);body.Children.Add(details);
        details.Text=string.Join("\n",actions.Select(a=>a.Path));preview.Click+=async(_,_)=>await Preview();apply.Click+=async(_,_)=>await Apply();individual.Click+=(_,_)=>{ReviewIndividually=true;Close();};Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Waiting for the current folder operation. Completed receipts are retained.";}};
    }
    private void Busy(bool value){busy=value;preview.IsEnabled=!value;individual.IsEnabled=!value;apply.IsEnabled=!value&&plans!=null;}
    private async Task Preview()
    {
        plans=null;Failure=null;Busy(true);
        try
        {
            var checkedPlans=await Task.Run(()=>actions.Select(a=>workspace.Preview(a)).ToList());
            if(checkedPlans.Select(p=>p.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=checkedPlans.Count)throw new IOException("Duplicate folder paths are not allowed in a group.");
            plans=checkedPlans;details.Text=string.Join("\n\n",plans.Select((p,i)=>$"{i+1}. {p.After}"));status.Text="Every path checked. Review the full list before creating these folders.";
        }
        catch(Exception e){plans=null;status.Text=e.Message+" Use individual review if a parent folder must be created first.";}
        finally{Busy(false);}
    }
    private async Task Apply()
    {
        if(plans==null||Receipts.Count>0)return;Busy(true);
        try
        {
            for(var i=0;i<plans.Count;i++)
            {
                var plan=plans[i];BeforeApply?.Invoke(i,plan);var receipt=await Task.Run(()=>workspace.Apply(plan,true));Receipts.Add(receipt);AfterApply?.Invoke(i,receipt);
            }
            busy=false;DialogResult=true;
        }
        catch(Exception e){Failure=e.Message;plans=null;status.Text=$"Stopped after {Receipts.Count} of {actions.Count} folders. "+e.Message+" Close this review and inspect the recorded receipts before retrying.";}
        finally{Busy(false);if(Receipts.Count>0){preview.IsEnabled=false;individual.IsEnabled=false;}}
    }
    internal Task TestPreview()=>Preview();
    internal Task TestApply()=>Apply();
    internal bool HasPreview=>plans!=null&&apply.IsEnabled;
}
