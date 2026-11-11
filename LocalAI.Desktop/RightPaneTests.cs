using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task ExerciseRightPane(Dictionary<string,object> rows)
    {
        chatProjectFilter=null;NewConversation();ShowChat();prompt.Text="KEEP_RIGHT_PANE_DRAFT";var owner=conversation;var windowCount=Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible);
        OpenCode("DOCUMENT_SNAPSHOT_MARKER","markdown",Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"Notes.md"),true);UpdateLayout();
        rows["documentPreviewOpensOnRightBesideVisibleChat"]=rightPaneBody.Content==documentPane&&documentText.IsReadOnly&&documentText.Text=="DOCUMENT_SNAPSHOT_MARKER"&&chatView.IsVisible&&documentPane.TranslatePoint(new Point(),this).X>chatView.TranslatePoint(new Point(),this).X&&prompt.Text=="KEEP_RIGHT_PANE_DRAFT";
        var documentFile=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"Notes.md");File.WriteAllText(documentFile,"DOCUMENT_SNAPSHOT_MARKER");var includedCount=files.Count;pickerTestAction=picker=>{picker.TestNavigate(Path.GetDirectoryName(documentFile)!);picker.TestChoose("Notes.md");};OpenDocument();pickerTestAction=null;rows["openDocumentPickerPreviewsWithoutAddingToModelContext"]=documentText.Text=="DOCUMENT_SNAPSHOT_MARKER"&&files.Count==includedCount&&conversation.Messages.Count==0;
        HideRightPane();rows["closingRightPaneRetainsChatAndDocumentDraft"]=chatView.IsVisible&&inspectorColumn.Width.Value==0&&prompt.Text=="KEEP_RIGHT_PANE_DRAFT";ShowRightPane("Document");
        NewConversation();rows["documentPreviewDoesNotLeakIntoAnotherChat"]=documentText.Text.Length==0;history.SelectedItem=owner;ShowRightPane("Document");rows["returningToChatRestoresItsOwnDocumentPreview"]=documentText.Text=="DOCUMENT_SNAPSHOT_MARKER"&&prompt.Text=="KEEP_RIGHT_PANE_DRAFT";
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var stopping=new CancellationTokenSource();var origin=$"http://127.0.0.1:{port}/";
        var serving=Task.Run(async()=>{while(!stopping.IsCancellationRequested){HttpListenerContext context;try{context=await listener.GetContextAsync().WaitAsync(stopping.Token);}catch(Exception e) when(e is OperationCanceledException or ObjectDisposedException or HttpListenerException){break;}var second=context.Request.RawUrl=="/second";var html="<!doctype html><title>"+(second?"Right Second":"Right Browser")+"</title><h1>RIGHT_PANE_PAGE_MARKER</h1><a href='/second'>Second page</a>";try{context.Response.ContentType="text/html";await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(html));context.Response.Close();}catch(HttpListenerException){}}});
        async Task Wait(Func<bool> check){for(var i=0;i<200;i++){if(check())return;await Task.Delay(50);}throw new IOException("Right browser fixture timed out: "+appBrowser?.Current?.CoreWebView2?.Source);}
        try
        {
            OpenBrowser(origin);await Wait(()=>appBrowser?.Current?.CoreWebView2?.DocumentTitle=="Right Browser");UpdateLayout();
            rows["browserLoadsActualPageInRightPaneWithoutAnotherWindow"]=rightPaneBody.Content==dockedBrowser&&chatView.IsVisible&&dockedBrowser!.IsVisible&&Window.GetWindow(dockedBrowser)==this&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==windowCount&&prompt.Text=="KEEP_RIGHT_PANE_DRAFT";
            var view=appBrowser!.Current;ShowRightPane("Workspace");OpenBrowser(origin+"second");await Wait(()=>appBrowser.Current?.CoreWebView2?.DocumentTitle=="Right Second");rows["rightBrowserRetainsSessionWhenSwitchingPaneTabs"]=appBrowser.Current==view&&view!.CanGoBack;await appBrowser.Control("back");await Wait(()=>view!.CoreWebView2.DocumentTitle=="Right Browser");
            var count=webSources.Count;appBrowser.ReviewTestAction=dialog=>{rows["dockedBrowserInclusionReviewOwnedByMainWindow"]=dialog.Owner==this;dialog.DialogResult=false;};await appBrowser.IncludePage();rows["dockedBrowserCancelDoesNotIncludeOrSendPage"]=webSources.Count==count&&conversation.Messages.Count==0;
            appBrowser.ReviewTestAction=dialog=>dialog.DialogResult=true;await appBrowser.IncludePage();appBrowser.ReviewTestAction=null;rows["dockedBrowserIncludesOnlyExplicitlyReviewedPageText"]=webSources.Count==count+1&&webSources.Last().Excerpt.Contains("RIGHT_PANE_PAGE_MARKER")&&conversation.Messages.Count==0;
            Width=1080;Height=700;UpdateLayout();rows["rightPaneFitsNarrowWindowAndKeepsComposerVisible"]=dockedBrowser!.TranslatePoint(new Point(dockedBrowser.ActualWidth,0),this).X<=ActualWidth&&prompt.IsVisible&&send.IsVisible;Capture(this,Path.ChangeExtension(smokeOutput!,".right-browser.png"));using(var file=File.Create(Path.ChangeExtension(smokeOutput!,".right-browser-page.png")))await view!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
            ShowRightPane("Document");UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".right-document.png"));
            OpenAgent();await AgentTool(new("browserOpen",origin),settings.Models.First(p=>!p.IsCloud).Id,null,CancellationToken.None);for(var i=0;i<200&&agentBrowser?.Current?.CoreWebView2?.DocumentTitle!="Right Browser";i++)await Task.Delay(50);UpdateLayout();rows["agentBrowserAlsoDocksBesideExistingMainSurface"]=AgentSurfaceActive&&Window.GetWindow(dockedBrowser)==this&&agentBrowser?.Current?.CoreWebView2?.DocumentTitle=="Right Browser"&&Application.Current.Windows.OfType<Window>().Count(w=>w.IsVisible)==windowCount;
        }
        finally{appBrowser?.Close();appBrowser=null;agentBrowser?.Close();agentBrowser=null;HideRightPane();ShowChat();stopping.Cancel();listener.Stop();await serving;}
    }
}
