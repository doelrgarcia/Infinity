using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private async Task ExerciseNestedProjectChats(Dictionary<string,object> rows)
    {
        var root=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"nested-chat-project");Directory.CreateDirectory(root);
        var source=Path.Combine(root,"Example.txt");File.WriteAllText(source,"PROJECT_CHAT_SOURCE");
        var project=CreateChatProject("Nested project chats",root,"SHARED_PROJECT_INSTRUCTIONS");
        Button Named(string name)=>Descendants<Button>(projectRows).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name);
        Button Chat(Guid id)=>Descendants<Button>(projectRows).Single(b=>b.Tag is Guid value&&value==id);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var first=conversation;first.Title="Planning";first.Messages.Add(new("user","FIRST_CHAT_ONLY"));store.Save(first);Render();prompt.Text="Planning draft";files.Add(new(source,"PROJECT_CHAT_SOURCE"));UpdateContext();
        Click(Named("New chat in project: "+project.Name));var second=conversation;
        rows["projectNewChatButtonCreatesIndependentPersistedChat"]=second.Id!=first.Id&&second.ProjectId==project.Id&&second.WorkspaceRoots.SequenceEqual(project.Roots)&&second.Messages.Count==0&&files.Count==0&&store.Load(out _).Any(c=>c.Id==second.Id);
        second.Title="Implementation";second.Messages.Add(new("assistant","SECOND_CHAT_ONLY"));store.Save(second);Render();prompt.Text="Implementation draft";
        var group=Descendants<StackPanel>(projectRows).Single(p=>p.Tag is Guid id&&id==project.Id);
        rows["multipleChatsRenderUnderTheirOwningProject"]=new[]{first,second}.All(c=>Chat(c.Id).IsDescendantOf(group))&&history.Visibility==Visibility.Collapsed&&recentChatsHeading.Visibility==Visibility.Collapsed;
        Click(Chat(first.Id));rows["nestedChatSwitchRestoresFirstHistoryDraftAndFiles"]=conversation==first&&prompt.Text=="Planning draft"&&files.Single().Path==source&&conversation.Messages.Single().Content=="FIRST_CHAT_ONLY";
        Click(Chat(second.Id));rows["nestedChatSwitchRestoresSecondWithoutFirstContext"]=conversation==second&&prompt.Text=="Implementation draft"&&files.Count==0&&conversation.Messages.Single().Content=="SECOND_CHAT_ONLY";
        rows["projectNotesSharedButMessagesRemainSeparate"]=BuildMessages("check").Any(m=>m.Content.Contains("SHARED_PROJECT_INSTRUCTIONS"))&&!BuildMessages("check").Any(m=>m.Content.Contains("FIRST_CHAT_ONLY"));
        Click(Named("Collapse chats in project: "+project.Name));
        rows["collapseHidesChildrenWithoutChangingActiveChatOrDraft"]=!Descendants<Button>(projectRows).Any(b=>b.Tag is Guid id&&id==first.Id)&&conversation==second&&prompt.Text=="Implementation draft";
        Click(Named("Expand chats in project: "+project.Name));
        rows["expandRestoresBothProjectChatRows"]=Chat(first.Id)!=null&&Chat(second.Id)!=null;
        historySearch.Text="Planning";
        rows["chatSearchShowsMatchingNestedChatOnly"]=Descendants<Button>(projectRows).Any(b=>b.Tag is Guid id&&id==first.Id)&&!Descendants<Button>(projectRows).Any(b=>b.Tag is Guid id&&id==second.Id)&&conversation==second;
        historySearch.Clear();
        await ApplyAppControl(new("renameChat","Implementation renamed"));
        rows["chatRenameUpdatesNestedRowAndSavedTitle"]=System.Windows.Automation.AutomationProperties.GetName(Chat(second.Id)).Contains("Implementation renamed")&&store.Load(out _).Single(c=>c.Id==second.Id).Title=="Implementation renamed";
        var otherRoot=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"other-chat-project");Directory.CreateDirectory(otherRoot);
        var other=CreateChatProject("Other nested project",otherRoot,"OTHER_PROJECT_INSTRUCTIONS");var third=conversation;
        rows["otherProjectChatIsNotListedUnderFirstProject"]=Chat(third.Id).IsDescendantOf(Descendants<StackPanel>(projectRows).Single(p=>p.Tag is Guid id&&id==other.Id))&&!Chat(third.Id).IsDescendantOf(Descendants<StackPanel>(projectRows).Single(p=>p.Tag is Guid id&&id==project.Id));
        await ApplyAppControl(new("switchChat",first.Id.ToString()));
        rows["appChatSwitchSelectsOwningProjectAndFolder"]=conversation==first&&chatProjectFilter==project.Id&&conversation.WorkspaceRoots.SequenceEqual(project.Roots)&&prompt.Text=="Planning draft"&&!BuildMessages("check").Any(m=>m.Content.Contains("OTHER_PROJECT_INSTRUCTIONS"));
        var saved=store.Load(out _);var owned=saved.Where(c=>c.ProjectId==project.Id).ToArray();
        rows["multipleChatLinksAndHistoriesSurviveStoreReload"]=owned.Length==2&&owned.Single(c=>c.Id==first.Id).Messages.Single().Content=="FIRST_CHAT_ONLY"&&owned.Single(c=>c.Id==second.Id).Messages.Single().Content=="SECOND_CHAT_ONLY"&&saved.Single(c=>c.Id==third.Id).ProjectId==other.Id;
        var count=conversations.Count;SetBusy(true);Click(Named("New chat in project: "+project.Name));SetBusy(false);
        rows["newProjectChatRespectsActiveWorkGuard"]=conversations.Count==count&&conversation==first;
        var previousHeight=Height;Height=MinHeight;UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Loaded);
        var searchBounds=historySearch.TransformToAncestor(this).TransformBounds(new Rect(historySearch.RenderSize));
        rows["projectChatTreeLeavesSearchReachableAtMinimumWindowHeight"]=searchBounds.Top>=0&&searchBounds.Bottom<ActualHeight-40;
        Height=previousHeight;status.Text="Project: "+project.Name;
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Loaded);UpdateLayout();QaCapture.Save(this,Path.ChangeExtension(smokeOutput!,".nested-chats.png"));
    }
}
