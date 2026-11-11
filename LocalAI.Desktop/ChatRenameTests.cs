using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private void ExerciseChatRename(Dictionary<string,object> rows)
    {
        var chat=conversation;var original=chat.Title;var id=chat.Id;var project=chat.ProjectId;var messageCount=chat.Messages.Count;var draft=prompt.Text;
        void InvokeRename()=>ChatMenu(chat).Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"Rename chat")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        renameChatTest=dialog=>{dialog.TestSubmit(" ");rows["emptyChatRenameStaysOpenWithoutChangingTitle"]=dialog.IsVisible&&dialog.TestError.Length>0&&chat.Title==original;dialog.TestSubmit("Renamed project chat");};
        try{InvokeRename();}finally{renameChatTest=null;}
        rows["renameMenuPersistsTitleWithoutChangingChatOrProject"]=chat.Title=="Renamed project chat"&&chat.Id==id&&chat.ProjectId==project&&chat.Messages.Count==messageCount&&prompt.Text==draft&&store.Load(out _).Single(c=>c.Id==id).Title==chat.Title;
        rows["renamedChatImmediatelyUpdatesNestedRow"]=Descendants<Button>(projectRows).Any(b=>b.Tag is Guid value&&value==id&&System.Windows.Automation.AutomationProperties.GetName(b).Contains("Renamed project chat"));
        renameChatTest=dialog=>dialog.Close();try{InvokeRename();}finally{renameChatTest=null;}
        rows["cancelRenamePreservesTitleAndDraft"]=chat.Title=="Renamed project chat"&&prompt.Text==draft;
        var path=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"smoke-conversations",id+".json");
        FileStream? locked=new(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            renameChatTest=dialog=>
            {
                dialog.TestSubmit("Retry rename");rows["failedRenameKeepsDialogOpenAndRollsBackTitle"]=dialog.IsVisible&&dialog.TestError.Contains(".lock")&&chat.Title=="Renamed project chat";
                locked.Dispose();locked=null;dialog.TestSubmit("Retry rename");
            };
            InvokeRename();
        }
        finally{locked?.Dispose();renameChatTest=null;}
        rows["renameRetryPersistsExactlyTheSameConversation"]=chat.Id==id&&chat.Title=="Retry rename"&&store.Load(out _).Count(c=>c.Id==id)==1;
        var bytes=File.ReadAllBytes(path);var externalStore=new ConversationStore(Path.GetDirectoryName(path)!);var external=externalStore.Load(out _).Single(c=>c.Id==id);external.Title="External title";externalStore.Save(external);
        try
        {
            renameChatTest=dialog=>{dialog.TestSubmit("Stale overwrite");rows["staleRenameRefusesToOverwriteExternalChat"]=dialog.IsVisible&&chat.Title=="Retry rename"&&new ConversationStore(Path.GetDirectoryName(path)!).Load(out _).Single(c=>c.Id==id).Title=="External title";dialog.Close();};InvokeRename();
        }
        finally{renameChatTest=null;File.WriteAllBytes(path,bytes);}
        var menu=ChatMenu(chat);rows["chatMenuKeepsDeleteBesideRename"]=menu.Items.OfType<MenuItem>().Select(i=>i.Header).SequenceEqual(new[]{"Rename chat","Delete chat"});
    }
    private async Task ExerciseMissingRuntime(Dictionary<string,object> rows)
    {
        var previous=model.SelectedItem;var oldHook=handoffTestAction;var handoffs=0;
        var socket=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);socket.Start();var port=((System.Net.IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
        var missing=new ModelProfile{Id="missing-runtime-fixture",Engine="ryzen",ModelPath=Path.GetDirectoryName(smokeOutput!)!,Executable=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"missing-server.exe"),Endpoint=$"http://127.0.0.1:{port}/",ContextLimit=4096};
        settings.Models.Add(missing);model.ItemsSource=null;model.ItemsSource=settings.Models;model.SelectedItem=missing;maximum.Text="64";contextBudget.Text="4096";conversation.AutoCliFallback=true;
        handoffTestAction=dialog=>{handoffs++;dialog.Close();};
        try{await ExecuteRequest(missing,"Synthetic missing runtime test",BuildMessages("Synthetic missing runtime test"),true,true);rows["localSetupFailureDoesNotOpenCloudHandoff"]=handoffs==0&&status.Text.Contains("Runtime or model missing");}
        finally{handoffTestAction=oldHook;settings.Models.Remove(missing);model.ItemsSource=null;model.ItemsSource=settings.Models;model.SelectedItem=previous;}
    }
}
