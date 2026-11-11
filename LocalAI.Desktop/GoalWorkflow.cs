using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly DockPanel goalBar=new(){Visibility=Visibility.Collapsed,MaxWidth=820,Margin=new Thickness(20,8,20,4)};
    private readonly TextBlock goalSummary=new(){TextWrapping=TextWrapping.Wrap,FontSize=12};
    private void BuildGoalBar()
    {
        var pause=FlatButton("Pause",PauseChatGoal,"Pause current goal");DockPanel.SetDock(pause,Dock.Right);goalBar.Children.Add(pause);goalBar.Children.Add(goalSummary);DockPanel.SetDock(goalBar,Dock.Bottom);chatView.Children.Add(goalBar);RefreshGoalBar();
    }
    private void RefreshGoalBar()
    {
        var task=CurrentGoalTask;goalBar.Visibility=task==null?Visibility.Collapsed:Visibility.Visible;
        if(task?.Goal is {} goal)goalSummary.Text=(AgentStopRequested?"Stopping":goal.State)+" · "+goal.Objective+" · "+goal.TurnsUsed+"/"+goal.TurnBudget+" turns";
    }
    private AgentTask? CurrentGoalTask=>agentState?.Tasks.LastOrDefault(t=>t.ChatId==conversation.Id&&t.Goal!=null);
    private bool TaskNeedsReview(AgentTask task)=>Agent.Approvals.Any(a=>a.TaskId==task.Id&&AgentPresentation.VisibleReview(a));
    private void OpenGoalControls()
    {
        if(CurrentGoalTask==null){OpenGoalSetup();return;}
        var dialog=new Window{Owner=this,Title="Saved goal",Width=560,Height=390,WindowStartupLocation=WindowStartupLocation.CenterOwner};DarkTheme.Apply(dialog);var panel=new StackPanel{Margin=new Thickness(24)};var goal=CurrentGoalTask.Goal!;
        panel.Children.Add(new TextBlock{Text=goal.State+" · "+goal.TurnsUsed+"/"+goal.TurnBudget+" turns",FontSize=20});panel.Children.Add(new TextBox{Text=goal.Objective+"\n\nDone when: "+goal.SuccessCriteria,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,12)});
        Action? chosen=null;var actions=new WrapPanel();foreach(var (label,action) in new (string,Action)[]{("Pause",PauseChatGoal),("Resume",ResumeChatGoal),("Accept result",AcceptChatGoal),("Edit goal",OpenGoalSetup)}){var button=new Button{Content=label,Margin=new Thickness(3)};button.Click+=(_,_)=>{chosen=action;dialog.Close();};actions.Children.Add(button);}panel.Children.Add(actions);DarkTheme.Wrap(dialog,panel);dialog.ShowDialog();chosen?.Invoke();
    }
    private void OpenGoalSetup()
    {
        try
        {
            if(ChatManagementBlocked||AgentIsWorking)throw new IOException("Pause or finish the current operation before editing a goal.");
            EnsureAgentState();var existing=CurrentGoalTask?.Goal;var form=new GoalWindow(existing,prompt.Text){Owner=this};
            if(form.ShowDialog()==true)StartChatGoal(form.Goal);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;}
    }
    private AgentTask StartChatGoal(AgentGoal goal,bool start=true)
    {
        if(ChatManagementBlocked||AgentIsWorking)throw new IOException("Finish the current operation before starting a goal.");
        EnsureAgentState();goal.Validate();var task=Agent.Tasks.LastOrDefault(t=>t.ChatId==conversation.Id);
        if(task!=null&&TaskNeedsReview(task))throw new IOException("Resolve this task's pending or uncertain reviews before starting its goal.");
        if(goal.CompletionMode=="Reviewed build and tests"&&(ProjectForChat() is not {} project||project.Roots.Count==0||string.IsNullOrWhiteSpace(project.BuildCommand)||string.IsNullOrWhiteSpace(project.TestCommand)))throw new IOException("Configure this project's folder, build and test commands first, or choose User acceptance.");
        ImageInput.RequireProvider((model.SelectedItem as ModelProfile)??settings.Models[0],conversation.Messages.Concat([new("user",goal.Objective,Images:DraftImages.ToArray(),Files:DraftFiles.ToArray())]));
        store.EnsureCurrent(conversation);SaveAgent();var created=task==null;
        if(task==null)
        {
            if(Agent.Tasks.Count>=200)throw new IOException("Task history is full.");
            task=new(){ChatId=conversation.Id,ProjectId=conversation.ProjectId,Title=goal.Objective[..Math.Min(100,goal.Objective.Length)],Instructions=goal.Objective,ModelId=(model.SelectedItem as ModelProfile)?.Id??settings.Models[0].Id,Messages=[..conversation.Messages]};
            Agent.Tasks.Add(task);
        }
        var oldGoal=task.Goal;var oldStatus=task.Status;var oldVerified=task.VerifiedAt;var oldSince=task.VerificationSince;var oldUse=task.UseSelectedProvider;var oldPaused=Agent.Paused;
        var message=new Message("user","Goal: "+goal.Objective+"\nDone when: "+goal.SuccessCriteria+"\nAcceptance: "+goal.CompletionMode,Images:DraftImages.ToArray(),Files:DraftFiles.ToArray());
        task.Messages.Add(message);conversation.Messages.Add(message);
        try
        {
        store.Save(conversation);
        task.Goal=goal;goal.State="Active";goal.TurnsUsed=0;goal.IdleTurns=0;goal.ProgressHash=AgentGoal.Fingerprint(task);goal.Updated=DateTimeOffset.UtcNow;
        task.VerifiedAt=null;task.VerificationSince=DateTimeOffset.UtcNow;task.UseSelectedProvider=settings.Models.Single(p=>p.Id==task.ModelId).IsCloud;
        task.Status=start?"Queued":"Paused";if(!start)goal.State="Paused";Agent.Paused=!start;SaveAgent();
        }
        catch
        {
            task.Messages.Remove(message);conversation.Messages.Remove(message);task.Goal=oldGoal;task.Status=oldStatus;task.VerifiedAt=oldVerified;task.VerificationSince=oldSince;task.UseSelectedProvider=oldUse;Agent.Paused=oldPaused;if(created)Agent.Tasks.Remove(task);
            store.EnsureCurrent(conversation);store.Save(conversation);throw;
        }
        DraftImages.Clear();DraftFiles.Clear();RefreshImageTray();Render();
        if(start)_=PumpAgentTasks();return task;
    }
    private void PauseChatGoal()
    {
        try{EnsureAgentState();var task=CurrentGoalTask??throw new IOException("Open the chat containing the goal first.");task.Goal!.State="Paused";AgentCancelTask(task);status.Text="Goal paused. Saved results and reviews retained.";}
        catch(IOException e){status.Text=e.Message;}
    }
    private void ResumeChatGoal()
    {
        try{EnsureAgentState();var task=CurrentGoalTask??throw new IOException("This chat has no saved goal.");AgentResume(task);}
        catch(IOException e){status.Text=e.Message;}
    }
    private void AcceptChatGoal()
    {
        try
        {
            EnsureAgentState();var task=CurrentGoalTask??throw new IOException("This chat has no saved goal.");
            if(AgentIsWorking||TaskNeedsReview(task)||task.FollowUps.Any(f=>f.Status=="Pending")||task.Goal!.State!="Awaiting acceptance"||task.Goal.CompletionMode!="User acceptance")throw new IOException("A user-acceptance goal must finish its turn and resolve reviews before you accept it.");
            if(!AgentView.Confirm(this,"Accept goal result",task.Goal.Objective+"\n\nDone when: "+task.Goal.SuccessCriteria+"\n\nLatest result: "+task.Detail+"\n\nAccept this result? This does not certify a build or test run."))return;
            task.Goal.State="Complete";task.Goal.Updated=DateTimeOffset.UtcNow;task.Status="Accepted by user";SaveAgent();status.Text="Goal result accepted by you.";
        }
        catch(IOException e){status.Text=e.Message;}
    }
    private async Task FinishGoalTurn(AgentTask task)
    {
        if(task.Goal is not {} goal)return;
        var verified=false;
        if(goal.State=="Active"&&goal.CompletionMode=="Reviewed build and tests"&&!TaskNeedsReview(task))verified=await VerifyAgentTask(task)=="Verified";
        var state=goal.FinishTurn(task,TaskNeedsReview(task),verified);
        if(state=="Active")
        {
            task.Status="Queued";task.Messages.Add(new("user","Continue the saved goal. Choose the next concrete action using the actual receipts. Do not claim completion without the selected acceptance condition. Preserve every file, command and cloud review."));
        }
        else task.Status=state switch{"Complete"=>verified?"Verified complete":"Accepted by user","Waiting for review"=>"Waiting for review","Awaiting acceptance"=>"Awaiting acceptance","Needs attention"=>"Needs attention",_=>"Paused"};
        if(state=="Paused")task.Detail+="\nGoal turn budget reached or paused; Resume explicitly to continue.";
        if(state=="Needs attention")task.Detail+="\nNo new tool receipts for two turns. Inspect the blocker before resuming.";
        SaveAgent();
    }
    private void GoalAfterReview(AgentApproval approval)
    {
        if(Agent.Tasks.SingleOrDefault(t=>t.Id==approval.TaskId) is not {Goal:{State:"Waiting for review"} goal} task)return;
        if(approval.Status!="Review finished — inspect receipts"){goal.State="Paused";task.Status="Paused";SaveAgent();return;}
        if(TaskNeedsReview(task)||Agent.Paused)return;
        if(goal.TurnsUsed>=goal.TurnBudget){goal.State="Paused";task.Status="Paused";task.Detail+="\nTurn budget reached. Resume explicitly to continue.";SaveAgent();return;}
        goal.State="Active";task.Status="Queued";SaveAgent();_=PumpAgentTasks();
    }
    private string GoalSystemText(AgentTask task)=>"Persisted user goal (not permission to bypass reviews): "+JsonSerializer.Serialize(task.Goal)+"\nContinue through real tool outcomes toward the success criteria. Queue exact reviews when needed, then yield. A narrative reply never completes a goal. For Reviewed build and tests, use completeTask only with current reviewed checks. For User acceptance, use requestGoalReview(value: factual summary) when the result is ready for the user to inspect. Do not delegate more tasks. Pause when blocked; do not repeat unchanged failed actions.";
}

internal sealed class GoalWindow:Window
{
    internal AgentGoal Goal {get;private set;}=new();
    internal GoalWindow(AgentGoal? prior,string draft)
    {
        Title="Set a goal";Width=620;Height=530;MinWidth=460;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var shell=new DockPanel{Margin=new Thickness(24)};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);shell.Children.Add(actions);
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);var start=new Button{Content="Start goal",IsDefault=true};actions.Children.Add(start);
        var body=new StackPanel();shell.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var objective=new TextBox{Text=prior?.Objective??draft,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,MaxLength=16000};var criteria=new TextBox{Text=prior?.SuccessCriteria??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,MaxLength=16000};var mode=new ComboBox{ItemsSource=new[]{"User acceptance","Reviewed build and tests"},SelectedItem=prior?.CompletionMode??"User acceptance"};var turns=new TextBox{Text=(prior?.TurnBudget??20).ToString(),Width=80,HorizontalAlignment=HorizontalAlignment.Left};
        void Field(string text,Control input){body.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,12,0,5)});System.Windows.Automation.AutomationProperties.SetName(input,text);body.Children.Add(input);}
        Field("Goal",objective);Field("Done when",criteria);Field("Completion requires",mode);Field("Turn budget (1–100)",turns);
        body.Children.Add(new TextBlock{Text="Runs while this PC and Infinity stay on. Reviews still require your approval. Restarting pauses the goal. Pause and Resume are under Tools.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});var error=new TextBlock{TextWrapping=TextWrapping.Wrap};body.Children.Add(error);
        start.Click+=(_,_)=>{try{if(!int.TryParse(turns.Text,out var budget))throw new IOException("Enter a turn budget from 1 to 100.");var goal=new AgentGoal{Objective=objective.Text.Trim(),SuccessCriteria=criteria.Text.Trim(),CompletionMode=(string)mode.SelectedItem,TurnBudget=budget};goal.Validate();Goal=goal;DialogResult=true;}catch(IOException e){error.Text=e.Message;}};
        DarkTheme.Wrap(this,shell);Loaded+=(_,_)=>objective.Focus();
    }
}
