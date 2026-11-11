using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task ProjectNavigationQa()
    {
        var rows=new Dictionary<string,object>();
        try
        {
            await ExerciseProjectNavigation(rows);
            var before=Agent.Projects.Count;var previous=conversation;var title="Save retry "+Guid.NewGuid().ToString("N")[..8];
            FileStream? locked=new(Path.Combine(AgentDirectory,"agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try
            {
                projectDialogTest=dialog=>
                {
                    dialog.TestSubmit(title,"","RETAINED_PROJECT_NOTES");
                    rows["failedSaveKeepsFormOpenWithExactError"]=dialog.IsVisible&&dialog.TestError.Contains("Could not create project:")&&dialog.TestError.Contains("agent.lock");
                    rows["failedSaveRetainsEntriesAndCurrentChat"]=dialog.ProjectName==title&&dialog.Notes=="RETAINED_PROJECT_NOTES"&&conversation==previous&&Agent.Projects.Count==before;
                    dialog.UpdateLayout();QaCapture.Save(dialog,Path.ChangeExtension(smokeOutput!,".save-error.png"));
                    locked.Dispose();locked=null;
                    dialog.TestSubmit(title,"","RETAINED_PROJECT_NOTES");
                    rows["retryClosesFormOnlyAfterProjectPersists"]=!dialog.IsVisible&&new AgentStore(AgentDirectory).Load().Projects.Count(p=>p.Name==title)==1;
                };
                OpenNewProject();
                rows["retryOpensExactlyOneProjectAndScopedChat"]=Agent.Projects.Count==before+1&&conversation.ProjectId==Agent.Projects.Single(p=>p.Name==title).Id&&status.Text=="Project created: "+title;
            }
            finally{locked?.Dispose();projectDialogTest=null;}
            for(var i=0;i<12;i++)Agent.Projects.Add(new(){Name="Earlier project "+i.ToString("D2")});SaveAgent();RefreshProjectNavigation();
            const string lastName="ZZZ New project visibility fixture";
            projectDialogTest=dialog=>dialog.TestSubmit(lastName);
            try{OpenNewProject();}finally{projectDialogTest=null;}
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Loaded);UpdateLayout();
            var projectViewport=Descendants<ScrollViewer>(this).Single(v=>ReferenceEquals(v.Content,projectRows));
            var selectedButton=Descendants<Button>(projectRows).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Open project: "+lastName);
            var bounds=selectedButton.TransformToAncestor(projectViewport).TransformBounds(new Rect(selectedButton.RenderSize));
            rows["newProjectIsActuallyVisibleInCrowdedSidebar"]=projectViewport.ScrollableHeight>0&&bounds.Top>=-1&&bounds.Bottom<=projectViewport.ActualHeight+1;
            rows["createdProjectHasVisibleSuccessMessage"]=Descendants<TextBlock>(this).Count(t=>t.IsVisible&&t.Text=="Project created: "+lastName)==1;
            var selectedProjectId=conversation.ProjectId;var savedProject=new AgentStore(AgentDirectory).Load().Projects.Single(p=>p.Name==lastName);
            rows["visibleNewProjectMatchesSavedProjectAndChat"]=savedProject.Id==selectedProjectId&&store.Load(out _).Any(c=>c.ProjectId==savedProject.Id);
            QaCapture.Save(this,Path.ChangeExtension(smokeOutput!,".selected-project.png"));
            await ExerciseNestedProjectChats(rows);
            ExerciseChatRename(rows);
            await ExerciseMissingRuntime(rows);
            await ExerciseDeletion(rows);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        File.WriteAllText(smokeOutput!,System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        var exit=rows.Count>0&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Close();Application.Current.Shutdown(exit);
    }
    private async Task ExerciseProjectNavigation(Dictionary<string,object> results)
    {
        bool Refused(Action action){try{action();return false;}catch(IOException){return true;}}
        chatProjectFilter=null;NewConversation();prompt.Text="Preserve my unrelated draft";var original=conversation;
        EnsureAgentState();var before=Agent.Projects.Count;var tasks=Agent.Tasks.Count;var activeProject=Agent.ActiveProjectId;var agentDraft=Agent.Draft;
        var create=Descendants<Button>(this).Single(b=>Equals(b.Tag,"New project"));
        projectDialogTest=dialog=>dialog.Close();create.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));projectDialogTest=null;
        results["cancelNewProjectPreservesStateAndDraft"]=Agent.Projects.Count==before&&conversation==original&&prompt.Text=="Preserve my unrelated draft";
        var title="Sidebar project "+Guid.NewGuid().ToString("N")[..8];
        projectDialogTest=dialog=>{dialog.TestSubmit("");results["emptyProjectNameStaysInDialog"]=dialog.IsVisible;dialog.UpdateLayout();QaCapture.Save(dialog,Path.ChangeExtension(smokeOutput!,".new-project.png"));dialog.TestSubmit(title,"","PROJECT_INSTRUCTIONS_FIXTURE");};
        create.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));projectDialogTest=null;UpdateLayout();
        var project=Agent.Projects.Single(p=>p.Name==title);var first=conversation;
        results["sidebarCreatesProjectAndScopedChat"]=chatProjectFilter==project.Id&&first.ProjectId==project.Id&&Descendants<Button>(projectRows).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Open project: "+title);
        results["projectAndChatPersistAcrossReload"]=new AgentStore(AgentDirectory).Load().Projects.Any(p=>p.Id==project.Id&&p.Purpose=="PROJECT_INSTRUCTIONS_FIXTURE")&&store.Load(out _).Any(c=>c.Id==first.Id&&c.ProjectId==project.Id);
        results["folderlessProjectHasNoFileOrCommandAuthority"]=project.Roots.Count==0&&conversation.WorkspaceRoots.Count==0&&!BuildMessages("hello")[0].Content.Contains("infinity-files");
        results["projectInstructionsEnterOnlyItsChats"]=BuildMessages("hello").Any(m=>m.Content.Contains("PROJECT_INSTRUCTIONS_FIXTURE"));
        prompt.Text="Project draft";NewConversation();results["newChatInProjectRetainsProjectScope"]=conversation.ProjectId==project.Id&&conversation.Id!=first.Id;
        OpenProjectChats(null);history.SelectedItem=original;results["projectNavigationPreservesOtherDraftAndContext"]=prompt.Text=="Preserve my unrelated draft"&&!BuildMessages("hello").Any(m=>m.Content.Contains("PROJECT_INSTRUCTIONS_FIXTURE"));
        OpenProjectChats(project.Id);history.SelectedItem=first;results["projectReopenRestoresDraftAndFiltersChats"]=prompt.Text=="Project draft"&&history.Items.Cast<Conversation>().All(c=>c.ProjectId==project.Id);
        results["projectCreationNeverStartsTasksOrChangesAgentDestination"]=Agent.Tasks.Count==tasks&&Agent.ActiveProjectId==activeProject&&Agent.Draft==agentDraft&&!AgentIsWorking;
        results["duplicateProjectNameRejectedWithoutOverwrite"]=Refused(()=>CreateChatProject(title.ToUpperInvariant(),"","overwrite"))&&Agent.Projects.Single(p=>p.Id==project.Id).Purpose=="PROJECT_INSTRUCTIONS_FIXTURE";
        results["invalidProjectFolderRejectedWithoutStateChange"]=Refused(()=>CreateChatProject("Missing folder",Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")),""))&&Agent.Projects.Count==before+1;
        SetBusy(true);results["projectCreationBlockedDuringActiveChat"]=Refused(()=>CreateChatProject("Busy project","",""));SetBusy(false);
        var root=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"sidebar-project-folder");Directory.CreateDirectory(root);
        projectDialogTest=dialog=>{dialog.TestChooseFolder(root);dialog.TestSubmit("Connected "+title,dialog.Folder);};create.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));projectDialogTest=null;var connected=Agent.Projects.Single(p=>p.Name=="Connected "+title);
        results["connectedProjectUsesOnlyChosenFolder"]=conversation.ProjectId==connected.Id&&conversation.WorkspaceRoots.SequenceEqual(new[]{root})&&BuildMessages("hello")[0].Content.Contains("infinity-files");
        var noFolder=new AgentTask{ProjectId=project.Id};project.BuildCommand="not executed";results["folderlessProjectBuildRequiresExplicitConnection"]=false;
        try{await ProposeTaskCheck(noFolder,"build");}catch(IOException e){results["folderlessProjectBuildRequiresExplicitConnection"]=e.Message.Contains("Connect a project folder");}finally{project.BuildCommand="";}
        var originalStore=agentStore;var originalState=agentState;
        try
        {
            var conflictRoot=Path.Combine(root,"conflict-fixture");agentStore=new AgentStore(conflictRoot);agentState=agentStore.Load();SaveAgent();
            var otherStore=new AgentStore(conflictRoot);var otherState=otherStore.Load();otherState.Memory="Concurrent fixture edit";otherStore.Save(otherState);
            results["projectSaveConflictRetainsExternalStateAndRollsBackMemory"]=Refused(()=>SaveProject(new(){Name="Must not persist"},false))&&Agent.Projects.Count==0&&new AgentStore(conflictRoot).Load().Memory=="Concurrent fixture edit";
        }
        finally{agentStore=originalStore;agentState=originalState;RefreshProjectNavigation();}
        OpenProjectChats(project.Id);UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".projects.png"));OpenProjectChats(null);
    }
}
