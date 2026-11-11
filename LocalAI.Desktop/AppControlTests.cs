using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    internal async Task RunAppControlTests(Dictionary<string,object> rows)
    {
        bool Refused(string json){try{AppControlProtocol.Parse(json);return false;}catch{return true;}}
        rows["unknownActionRejected"]=Refused("{\"action\":\"deleteChat\"}");
        rows["unknownAndDuplicateFieldsRejected"]=Refused("{\"action\":\"newChat\",\"script\":\"bad\"}")&&Refused("{\"action\":\"newChat\",\"action\":\"listChats\"}");
        rows["quotedAndSourceRequestsNotRouted"]=AppControlProtocol.FromPrompt("Explain how to open a new chat")==null&&AppControlProtocol.FromPrompt("```\nopen a new chat\n```")==null&&AppControlProtocol.FromPrompt("\"open a new chat\"")==null;
        rows["naturalSessionAndPoliteCommandsParsed"]=AppControlProtocol.FromPrompt("Please open a new session called Acme")==new AppControlRequest("newChat","Acme")&&AppControlProtocol.FromPrompt("Can you list my chats?")?.Action=="listChats";
        rows["invalidViewAndMultilineRejected"]=Refused("{\"action\":\"showView\",\"value\":\"terminal\"}")&&Refused("{\"action\":\"renameChat\",\"value\":\"a\\nb\"}");
        NewConversation();var origin=conversation;origin.Title="App QA origin";origin.TaskBrief="Private source-chat brief";origin.WorkspaceRoots.Add(Path.GetTempPath());var source=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"Private.cs");File.WriteAllText(source,"SOURCE PRIVATE");files.Add(ContextFiles.Read(source));store.Save(origin);prompt.Text="open a new session called Acme QA";var count=conversations.Count;await Send();var acme=conversation;
        rows["sendCreatesNamedChatWithoutModelCall"]=acme.Id!=origin.Id&&acme.Title=="Acme QA"&&conversations.Count==count+1&&acme.Messages.Count==0&&prompt.Text=="";
        rows["newSessionIsolatesSourceContext"]=acme.WorkspaceRoots.Count==0&&files.Count==0&&acme.TaskBrief==""&&!BuildMessages("test").Any(m=>m.Content.Contains("SOURCE PRIVATE"));
        rows["newChatPersistedImmediately"]=store.Load(out _).Any(c=>c.Id==acme.Id&&c.Title=="Acme QA");
        prompt.Text="rename this chat to Acme planning";await Send();rows["renamePersistsAndUpdatesHeading"]=conversation.Title=="Acme planning"&&chatHeading.Text=="Acme planning"&&store.Load(out _).Any(c=>c.Id==acme.Id&&c.Title=="Acme planning");
        prompt.Text="use Plan mode";await Send();rows["responseModePersists"]=conversation.ResponseStyle=="Plan"&&responseStyle.SelectedItem as string=="Plan"&&store.Load(out _).Single(c=>c.Id==acme.Id).ResponseStyle=="Plan";
        prompt.Text="select model "+settings.Models.Last().Id;await Send();rows["selectModelDoesNotSend"]=ReferenceEquals(model.SelectedItem,settings.Models.Last())&&conversation.Messages.Count==0&&store.Load(out _).Single(c=>c.Id==acme.Id).ModelId==settings.Models.Last().Id;
        rows["naturalAgentViewCommandParsed"]=AppControlProtocol.FromPrompt("show agent")==new AppControlRequest("showView","agent");prompt.Text="show agent";await Send();rows["agentViewControlSharesWindowWithoutModelCall"]=AgentSurfaceActive&&Window.GetWindow(agentView)==this&&conversation.Messages.Count==0&&Agent.Tasks.Count==0;
        prompt.Text="show code";await Send();var codeOpen=codeView.Visibility==Visibility.Visible;prompt.Text="show sources";await Send();rows["viewControlsOpenPanels"]=codeOpen&&chatView.Visibility==Visibility.Visible&&ReferenceEquals(inspectorContent.Content,sourcesInspector);
        prompt.Text="Draft to keep in Acme";await ApplyAppControl(new("switchChat",origin.Id.ToString()));
        rows["switchRestoresOriginalFilesAndBrief"]=conversation.Id==origin.Id&&files.Any(f=>f.Content=="SOURCE PRIVATE")&&briefEditor.Text==origin.TaskBrief;
        prompt.Text="switch to chat Acme planning";await Send();rows["switchRestoresUnsentTargetDraft"]=conversation.Id==acme.Id&&prompt.Text=="Draft to keep in Acme";
        var duplicate=new Conversation{Title="Acme planning"};store.Save(duplicate);conversations.Add(duplicate);prompt.Text="switch to chat Acme planning";await Send();rows["ambiguousTitleDoesNotSwitch"]=conversation.Id==acme.Id&&prompt.Text.Contains("switch")&&status.Text.Contains("More than one");
        prompt.Text="switch to chat Missing QA chat";await Send();rows["missingChatKeepsPrompt"]=conversation.Id==acme.Id&&prompt.Text.Contains("Missing")&&status.Text.Contains("not found");
        prompt.Text="list my chats";await Send();rows["listContainsOnlyTitlesAndIds"]=conversation.Messages.Last().Content.Contains(origin.Id.ToString())&&!conversation.Messages.Last().Content.Contains("SOURCE PRIVATE")&&!conversation.Messages.Last().Content.Contains(origin.TaskBrief);
        prompt.Text="Pending draft must survive model proposal";var proposal="{\"action\":\"renameChat\",\"value\":\"Approved name\"}";var before=conversation.Title;
        conversation.Messages.Add(new("assistant","```infinity-app\n"+proposal+"\n```"));Render();rows["modelProposalDoesNotAutoExecute"]=conversation.Title==before;
        var apply=Descendants<Button>(transcript).Single(b=>Equals(b.Tag,"Apply app action"));apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));rows["explicitApplyRunsAndPreservesComposer"]=conversation.Title=="Approved name"&&prompt.Text=="Pending draft must survive model proposal";
        conversation.Messages.Clear();conversation.Messages.Add(new("user","```infinity-app\n"+proposal+"\n```"));Render();rows["userQuotedProposalHasNoApplyButton"]=!Descendants<Button>(transcript).Any(b=>Equals(b.Tag,"Apply app action"));
        await ApplyAppControlProposal(proposal,origin.Id);rows["staleProposalCannotAffectOtherChat"]=conversation.Title=="Approved name"&&status.Text.Contains("different chat");
        SetBusy(true);await ApplyAppControl(new("renameChat","Must not rename"));SetBusy(false);rows["busyAppRejectsControlActions"]=conversation.Title=="Approved name";
        rows["allProfilesReceiveAppProtocol"]=BuildMessages("test")[0].Content.Contains(AppControlProtocol.Language);
        prompt.Clear();var voiceChatCount=conversations.Count;var spoken=new TestDictation();voiceSessionFactory=()=>spoken;ToggleDictation();spoken.Say("Open a new chat called Voice initiated QA");await Send();rows["voiceControlWaitsForStopAndSend"]=conversations.Count==voiceChatCount&&dictation!=null;
        ToggleDictation();voiceSessionFactory=null;await Send();rows["spokenPromptCreatesChatAfterStopAndSend"]=conversation.Title=="Voice initiated QA"&&conversations.Count==voiceChatCount+1&&dictation==null;
        conversation.Messages.Clear();prompt.Clear();ShowChat();UpdateLayout();
    }
}
internal static class AppControlTests
{
    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var rows=new Dictionary<string,object>();MainWindow? window=null;
        var run=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"app-controls-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
        try{window=new MainWindow(["--ui-session",Path.Combine(run,"session.json")]);window.Show();await window.RunAppControlTests(rows);QaCapture.Save(window,Path.ChangeExtension(output,".png"));rows["appControlsDarkRender"]=true;}catch(Exception e){rows["error"]=e.ToString();}finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==28&&rows.Values.All(v=>v is true)?0:1);
    }
}
