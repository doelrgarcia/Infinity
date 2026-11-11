using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalAI.Core;

namespace LocalAI.Desktop;

internal static class UiDialogPathTests
{
    internal static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        var seen=new HashSet<DependencyObject>();var pending=new Stack<DependencyObject>();pending.Push(root);
        while(pending.TryPop(out var item))
        {
            if(!seen.Add(item))continue;if(item is T match)yield return match;
            if(item is Visual)for(var i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)pending.Push(VisualTreeHelper.GetChild(item,i));
            if(item is FrameworkElement)foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())pending.Push(child);
        }
    }
    private static Button Button(Window window,string label)=>Find<Button>(window).Single(b=>Equals(b.Content,label));
    private static TextBox Field(Window window,string name)=>Find<TextBox>(window).Single(t=>AutomationProperties.GetName(t)==name);
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"dialog-fixture");Directory.CreateDirectory(root);
        void Check(string key,bool value){rows[key]=value;if(!value)throw new IOException("UI dialog assertion failed: "+key);}
        async Task Wait(Func<bool> predicate){for(var n=0;n<400;n++){if(predicate())return;await Task.Delay(50);}throw new IOException("UI dialog fixture timed out.");}
        CommandWindow? command=null;BrowserWindow? browser=null;MainWindow? dock=null;HttpListener? server=null;Task? serving=null;using var stop=new CancellationTokenSource();
        try
        {
            var included=new List<CommandRun>();command=new([root],includeResult:included.Add,logRoot:Path.Combine(root,"command-logs"));command.Show();
            var script=Field(command,"Command script");var timeout=Field(command,"Command time limit");var folder=Field(command,"Command starting folder");
            var review=Button(command,"Review exact command");var run=Button(command,"Run reviewed command");var cancel=Button(command,"Stop run");var include=Button(command,"Include result in chat");var consent=Find<CheckBox>(command).Single();
            Check("commandInitialStateDisablesRunStopInclude",!run.IsEnabled&&!cancel.IsEnabled&&!include.IsEnabled);
            timeout.Text="invalid";UiPathTests.Click(review);Check("invalidTimeLimitCannotEnableRun",!run.IsEnabled&&Find<TextBlock>(command).Any(t=>t.Text=="Enter a whole-number time limit."));
            timeout.Text="30";script.Text="[Console]::WriteLine('UI_BUTTON_RESULT')";UiPathTests.Click(review);
            Check("reviewButtonDoesNotExecute",command.TestReviewed&&command.Result==null);
            Check("reviewIncludesExactScriptAndFolder",Field(command,"Exact command review").Text.Contains(script.Text)&&Field(command,"Exact command review").Text.Contains(root));
            UiPathTests.Click(consent);Check("explicitConsentEnablesRun",run.IsEnabled);
            timeout.Text="31";Check("timeoutEditRevokesConsent",!run.IsEnabled&&consent.IsChecked==false);
            UiPathTests.Click(review);UiPathTests.Click(consent);folder.Text=Path.GetDirectoryName(root)!;Check("folderEditRevokesConsent",!run.IsEnabled&&consent.IsChecked==false);
            UiPathTests.Click(review);Check("unconnectedFolderRefusesReview",!command.TestReviewed&&!run.IsEnabled);
            folder.Text=root;UiPathTests.Click(review);UiPathTests.Click(consent);UiPathTests.Click(run);
            Check("runButtonLocksDraftAndEnablesStop",!script.IsEnabled&&cancel.IsEnabled&&!run.IsEnabled);
            await Wait(()=>cancel.IsEnabled==false);
            Check("runButtonProducesActualSuccessfulOutput",command.Result is {Status:"Succeeded"} result&&result.Output.Contains("UI_BUTTON_RESULT"));
            Check("runCompletionRestoresEditingAndRevokesConsent",script.IsEnabled&&folder.IsEnabled&&timeout.IsEnabled&&consent.IsChecked==false&&!run.IsEnabled);
            Check("resultNotAutomaticallyIncluded",included.Count==0&&include.IsEnabled);
            UiPathTests.Click(include);Check("includeButtonAddsExactResultOnce",included.Count==1&&ReferenceEquals(included[0],command.Result)&&!include.IsEnabled);
            script.Text="exit 7";UiPathTests.Click(review);UiPathTests.Click(consent);UiPathTests.Click(run);await Wait(()=>!cancel.IsEnabled);
            Check("nonzeroExitIsReportedAsFailure",command.Result is {ExitCode:7} failed&&failed.Status!="Succeeded");
            script.Text="[Console]::WriteLine('STARTED'); [Threading.Thread]::Sleep(20000)";UiPathTests.Click(review);UiPathTests.Click(consent);UiPathTests.Click(run);
            command.Close();Check("windowCannotCloseDuringCommand",command.IsVisible&&cancel.IsEnabled);
            UiPathTests.Click(cancel);await Wait(()=>!cancel.IsEnabled);Check("stopButtonCancelsCommand",command.Result is {} stopped&&stopped.Status!="Succeeded");
            Check("cancelledResultHasDurableLog",command.Result!=null&&File.Exists(command.Result.LogPath));command.Close();command=null;

            var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();var origin=$"http://127.0.0.1:{port}/";server=new();server.Prefixes.Add(origin);server.Start();var responses=0;
            serving=Task.Run(async()=>
            {
                while(!stop.IsCancellationRequested)
                {
                    HttpListenerContext context;try{context=await server.GetContextAsync().WaitAsync(stop.Token);}catch(Exception e) when(e is OperationCanceledException or HttpListenerException or ObjectDisposedException){break;}
                    var bytes=Encoding.UTF8.GetBytes("<!doctype html><html><head><title>UI "+(context.Request.RawUrl=="/second"?"Second":"First")+"</title></head><body>SYNTHETIC_BROWSER_RESULT</body></html>");
                    try{context.Response.ContentType="text/html";await context.Response.OutputStream.WriteAsync(bytes);Interlocked.Increment(ref responses);context.Response.Close();}catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){context.Response.Abort();}
                }
            });
            var sources=new List<WebSource>();var externalRequests=new List<System.Diagnostics.ProcessStartInfo>();browser=new(sources.Add,testProfile:Path.Combine(root,"browser-profile"),externalStart:externalRequests.Add);browser.Show();
            await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="Infinity Browser");
            var address=Field(browser,"Browser address");var tabs=Find<TabControl>(browser).Single();
            Check("browserWelcomeLoadsWithEmptyAddress",address.Text.Length==0);
            Check("initialBrowserDisablesHistoryButtons",!Button(browser,"←").IsEnabled&&!Button(browser,"→").IsEnabled);
            address.Text=origin;UiPathTests.Click(Button(browser,"Go"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI First");
            Check("browserGoButtonNavigatesEnteredAddress",browser.Current!.CoreWebView2.Source==origin);
            address.Text=origin+"second";UiPathTests.Click(Button(browser,"Go"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI Second");
            UiPathTests.Click(Button(browser,"←"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI First");Check("browserBackButtonReturnsToPreviousPage",Button(browser,"→").IsEnabled);
            UiPathTests.Click(Button(browser,"→"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI Second");Check("browserForwardButtonReturnsToNextPage",browser.Current!.CoreWebView2.Source==origin+"second");
            UiPathTests.Click(Button(browser,"Open externally"));Check("browserExternalButtonDispatchesOnlyCurrentUrl",externalRequests.Count==1&&externalRequests[0].FileName==origin+"second"&&externalRequests[0].UseShellExecute);
            var before=responses;UiPathTests.Click(Button(browser,"Reload"));await Wait(()=>responses>before);Check("browserReloadButtonRequestsPageAgain",responses>before);
            await Task.Delay(150);var source=browser.Current.CoreWebView2.Source;address.Text="file:///C:/Windows/win.ini";UiPathTests.Click(Button(browser,"Go"));Check("browserGoRejectsFileSchemeWithoutNavigating",browser.Current.CoreWebView2.Source==source&&Find<TextBlock>(browser).Any(t=>t.Text.Contains("HTTP")));
            var blockedUri="data:text/html,<h1>untrusted</h1>";var blocked=new TaskCompletionSource<bool>();browser.Current.CoreWebView2.NavigationStarting+=(_,e)=>{if(e.Uri.StartsWith("data:text/html,"))blocked.TrySetResult(e.Cancel);};browser.Current.CoreWebView2.Navigate(blockedUri);
            Check("browserBlocksArbitraryPageInitiatedDataUri",await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            browser.ReviewTestAction=dialog=>dialog.Close();UiPathTests.Click(Button(browser,"Include page"));await Task.Delay(250);Check("browserIncludeCancellationKeepsSourcesEmpty",sources.Count==0);
            browser.ReviewTestAction=dialog=>UiPathTests.Click(Button(dialog,"Include this text in chat"));UiPathTests.Click(Button(browser,"Include page"));await Wait(()=>sources.Count==1);Check("browserIncludeButtonsReturnReviewedActualText",sources[0].Excerpt.Contains("SYNTHETIC_BROWSER_RESULT")&&sources[0].Url==source);browser.ReviewTestAction=null;
            UiPathTests.Click(Button(browser,"Stop"));Check("browserStopButtonLeavesWindowUsable",browser.IsVisible&&Button(browser,"Go").IsEnabled);
            var firstView=browser.Current;UiPathTests.Click(Button(browser,"+ Tab"));await Wait(()=>tabs.Items.Count==2&&browser.Current?.CoreWebView2!=null);Check("browserNewTabButtonCreatesSelectedView",browser.Current!=firstView);
            UiPathTests.Click(Button(browser,"Close tab"));Check("browserCloseTabButtonReturnsToPreviousView",tabs.Items.Count==1&&browser.Current==firstView);
            for(var n=2;n<=8;n++){UiPathTests.Click(Button(browser,"+ Tab"));var count=n;await Wait(()=>tabs.Items.Count==count&&browser.Current?.CoreWebView2!=null);}
            UiPathTests.Click(Button(browser,"+ Tab"));Check("browserTabLimitShowsActionableMessage",tabs.Items.Count==8&&Find<TextBlock>(browser).Any(t=>t.Text.Contains("maximum 8")));
            while(tabs.Items.Count>1)UiPathTests.Click(Button(browser,"Close tab"));
            UiPathTests.Click(Button(browser,"Close tab"));await Wait(()=>tabs.Items.Count==1&&browser.Current?.CoreWebView2!=null);Check("closingLastTabCreatesUsableWelcomeTab",browser.Current!=firstView);
            browser.UpdateLayout();QaCapture.Save(browser,Path.ChangeExtension(output,".browser.png"));
            dock=new MainWindow(["--ui-session",Path.Combine(root,"docked-session","session.json")]);dock.Show();dock.DockBrowserForTest(browser);await Task.Delay(100);
            Check("browserToolbarRunsInsideActualMainWindow",Find<Button>(dock).Any(b=>Equals(b.Content,"Go"))&&browser.Owner==dock&&!browser.IsVisible);
            address.Text=origin;UiPathTests.Click(Button(dock,"Go"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI First");Check("dockedBrowserGoButtonLoadsActualPage",browser.Current!.CoreWebView2.Source==origin);
            address.Text=origin+"second";UiPathTests.Click(Button(dock,"Go"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI Second");UiPathTests.Click(Button(dock,"←"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI First");Check("dockedBrowserBackButtonNavigatesHistory",Button(dock,"→").IsEnabled);
            UiPathTests.Click(Button(dock,"→"));await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="UI Second");Check("dockedBrowserForwardButtonNavigatesHistory",browser.Current!.CoreWebView2.Source==origin+"second");
            before=responses;UiPathTests.Click(Button(dock,"Reload"));await Wait(()=>responses>before);Check("dockedBrowserReloadButtonRequestsPageAgain",responses>before);
            UiPathTests.Click(Button(dock,"Open externally"));Check("dockedBrowserExternalButtonDispatchesExactUrl",externalRequests.Count==2&&externalRequests[^1].FileName==origin+"second");
            browser.ReviewTestAction=dialog=>UiPathTests.Click(Button(dialog,"Include this text in chat"));UiPathTests.Click(Button(dock,"Include page"));await Wait(()=>sources.Count==2);Check("dockedBrowserIncludeButtonAddsReviewedPageText",sources[^1].Excerpt.Contains("SYNTHETIC_BROWSER_RESULT"));browser.ReviewTestAction=null;
            firstView=browser.Current;UiPathTests.Click(Button(dock,"+ Tab"));await Wait(()=>tabs.Items.Count==2&&browser.Current?.CoreWebView2!=null);Check("dockedBrowserNewTabButtonSelectsNewView",browser.Current!=firstView);
            UiPathTests.Click(Button(dock,"Close tab"));Check("dockedBrowserCloseTabButtonRestoresPriorView",tabs.Items.Count==1&&browser.Current==firstView);
            UiPathTests.Click(Find<Button>(dock).Single(b=>Equals(b.Content,"Stop")&&b.IsVisible));Check("dockedBrowserStopButtonLeavesOwnerUsable",dock.IsVisible&&Button(dock,"Go").IsEnabled);
        }
        catch(Exception error){rows["error"]=error.ToString();if(browser!=null){rows["browserStatus"]=string.Join(" | ",Find<TextBlock>(browser).Select(t=>t.Text));rows["browserSource"]=browser.Current?.CoreWebView2?.Source??"uninitialized";QaCapture.Save(browser,Path.ChangeExtension(output,".failure.png"));}}
        finally
        {
            command?.CancelForOwner();command?.Close();dock?.Close();browser?.Close();stop.Cancel();server?.Close();if(serving!=null)await serving;
        }
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==43&&rows.Values.All(v=>v is true)?0:1);
    }
}

public sealed partial class MainWindow
{
    internal void DockBrowserForTest(BrowserWindow browser)
    {
        browser.Hide();browser.Owner=this;appBrowser=browser;ShowRightPane("Browser",browser.DockContent(this));
    }
}
