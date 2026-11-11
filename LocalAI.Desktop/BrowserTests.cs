using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal static class BrowserTests
{
    internal static async Task Run(string output,bool live=false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var rows=new Dictionary<string,object>();BrowserWindow? browser=null;var stop=new CancellationTokenSource();HttpListener? listener=null;Task? serving=null;
        bool Refused(string value){try{BrowserAddress.Normalize(value);return false;}catch{return true;}}
        try
        {
            rows["unsafeBrowserSchemesRejected"]=Refused("file:///C:/Windows/win.ini")&&Refused("javascript:alert(1)")&&Refused("data:text/html,bad")&&Refused("https://user:secret@example.com");
            rows["browserAddressesNormalized"]=BrowserAddress.Normalize("example.com")=="https://example.com/"&&BrowserAddress.Normalize("localhost:8041")=="http://localhost:8041/";
            rows["browserPromptRoutesWithoutModel"]=AppControlProtocol.FromPrompt("Open website https://example.com")?.Action=="openBrowser";
            var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();var origin=$"http://127.0.0.1:{port}/";listener=new();listener.Prefixes.Add(origin);listener.Start();
            serving=Task.Run(async()=>{while(!stop.IsCancellationRequested){HttpListenerContext context;try{context=await listener.GetContextAsync().WaitAsync(stop.Token);}catch(Exception e) when(e is OperationCanceledException or HttpListenerException or ObjectDisposedException){break;}var path=context.Request.RawUrl;var body=Encoding.UTF8.GetBytes("<!doctype html><html><head><title>"+(path=="/second"?"Acme Second":"Acme Browser")+"</title></head><body><h1>Acme browser fixture</h1><p>This page is synthetic QA text.</p><a href='/second'>More Acme</a></body></html>");context.Response.ContentType="text/html";context.Response.ContentLength64=body.Length;try{await context.Response.OutputStream.WriteAsync(body);}finally{context.Response.Close();}}});
            var included=new List<WebSource>();browser=new BrowserWindow(source=>included.Add(source),testProfile:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"browser-profile-"+Guid.NewGuid().ToString("N")));browser.CoreReadyTestAction=()=>browser.Navigate(origin);browser.Show();
            async Task Wait(Func<bool> check){for(var n=0;n<200;n++){if(check())return;await Task.Delay(100);}throw new IOException("Browser fixture timed out: "+browser.Current?.CoreWebView2?.DocumentTitle+" / "+browser.Current?.CoreWebView2?.Source);}
            await Wait(()=>browser.Current?.CoreWebView2!=null);var core=browser.Current!.CoreWebView2;
            rows["embeddedBrowserRuntimeStarts"]=core.Environment.BrowserVersionString.Length>0;
            await Wait(()=>core.DocumentTitle=="Acme Browser");browser.CoreReadyTestAction=null;rows["addressNavigationLoadsActualPage"]=core.Source==origin;
            var text=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.body.innerText"));rows["actualPageTextReadable"]=text?.Contains("synthetic QA")==true;
            var links=JsonDocument.Parse(await browser.Control("links"));var linkId=links.RootElement[0].GetProperty("id").GetString()!;await browser.FollowLink(linkId);await Wait(()=>core.DocumentTitle=="Acme Second");rows["aiLinkIdNavigates"]=core.Source.EndsWith("/second");await browser.Control("back");await Wait(()=>core.DocumentTitle=="Acme Browser");
            try{await browser.FollowLink(linkId);rows["staleBrowserLinkRejected"]=false;}catch(IOException){rows["staleBrowserLinkRejected"]=true;}
            await browser.FollowLink("More Acme",true);await Wait(()=>core.DocumentTitle=="Acme Second");rows["directNamedLinkNavigates"]=true;await browser.Control("back");await Wait(()=>core.DocumentTitle=="Acme Browser");
            var bg=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor"));rows["pageBackgroundForcedDark"]=bg=="rgb(17, 21, 30)";
            browser.Navigate(origin+"second");await Wait(()=>core.DocumentTitle=="Acme Second");rows["backNavigationAvailable"]=browser.Current.CanGoBack;await browser.Control("back");await Wait(()=>core.DocumentTitle=="Acme Browser");rows["backNavigationRestoresPage"]=browser.Current.CanGoForward;await browser.Control("forward");await Wait(()=>core.DocumentTitle=="Acme Second");rows["forwardNavigationRestoresPage"]=core.Source.EndsWith("/second");
            rows["browsingDoesNotAutoInclude"]=included.Count==0;browser.ReviewTestAction=w=>w.DialogResult=false;await browser.IncludePage();rows["cancelPageReviewDoesNotInclude"]=included.Count==0;
            browser.ReviewTestAction=w=>w.DialogResult=true;await browser.IncludePage();browser.ReviewTestAction=null;rows["reviewedPageTextIncluded"]=included.Count==1&&included[0].Url==origin+"second"&&included[0].Excerpt.Contains("synthetic QA");
            var initial=browser.Current;await browser.Control("newTab");browser.Navigate(origin);await Wait(()=>browser.Current?.CoreWebView2?.DocumentTitle=="Acme Browser");rows["newTabOpensInsideApp"]=browser.Current!=initial;await browser.Control("closeTab");rows["closeTabReturnsToPrevious"]=browser.Current==initial;
            rows["noHostBridgeExposed"]=!core.Settings.AreHostObjectsAllowed&&!core.Settings.IsWebMessageEnabled;
            rows["profileUsesDarkScheme"]=core.Profile.PreferredColorScheme==Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
            QaCapture.Save(browser,Path.ChangeExtension(output,".png"));using(var image=File.Create(Path.ChangeExtension(output,".page.png")))await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,image);rows["browserDarkRenderCaptured"]=true;
            if(live){browser.Navigate("https://example.com/");await Wait(()=>core.DocumentTitle=="Example Domain");rows["liveWebsiteLoads"]=core.Source.StartsWith("https://example.com");}
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{browser?.Close();stop.Cancel();listener?.Close();if(serving!=null)try{await serving;}catch(Exception e){rows["serverCleanupError"]=e.Message;}stop.Dispose();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==(live?22:21)&&rows.Values.All(v=>v is true)?0:1);
    }
}
