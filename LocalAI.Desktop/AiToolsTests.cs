using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class AiToolsTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Children<T>(child))yield return nested;}}
    private static void Capture(Window window,string path)=>QaCapture.Save(window,path);
    // Explicit live smoke only: never part of the hermetic verifier.
    internal static async Task RunDesktop(string output)
    {
        var results=new Dictionary<string,object>();AiToolsWindow? window=null;
        try
        {
            if(!Environment.UserName.Equals("WDAGUtilityAccount",StringComparison.OrdinalIgnoreCase)||!File.Exists(@"C:\Results\guest-identity.txt"))throw new IOException("Live desktop launch QA is restricted to the disposable Windows Sandbox user session.");
            var settings=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));var starts=new List<object>();
            window=new AiToolsWindow(settings,info=>{if(info.FileName.StartsWith("https:",StringComparison.OrdinalIgnoreCase))throw new IOException("Desktop is absent; website fallback is not an installed-desktop launch pass.");using var requested=Process.Start(info);starts.Add(new{Executable=info.FileName,Arguments=info.ArgumentList.ToArray(),RequestProcessId=requested?.Id});});window.Show();
            foreach(var provider in new[]{"cursor","grok","codex","deepseek"})
            {
                var executable=AiLaunchers.DesktopPath(provider)??throw new IOException("Missing desktop: "+provider);
                window.TestLaunchButtons(provider,"Open desktop");if(!window.LaunchResults.TryGetValue(provider+" / Open desktop",out var launched)||!launched)throw new IOException("Desktop request failed: "+provider);
                object[] evidence=[];
                for(var attempt=0;attempt<30;attempt++)
                {
                    await Task.Delay(1000);var snapshots=new List<object>();
                    foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
                    using(process){try{process.Refresh();if(!process.HasExited&&process.MainWindowHandle!=0)snapshots.Add(new{ProcessId=process.Id,Executable=process.MainModule?.FileName,WindowHandle=process.MainWindowHandle.ToInt64(),Title=process.MainWindowTitle,Responsive=process.Responding});}catch(System.ComponentModel.Win32Exception){}}
                    if(snapshots.Count>0){evidence=snapshots.ToArray();break;}
                }
                results[provider+" desktop route has live window"]=evidence.Length>0;results[provider+" window metadata"]=evidence;
            }
            var existingPorts=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(p=>p.Port).ToHashSet();
            window.TestLaunchButtons("deepseek","Open interactive CLI");
            var webReady=false;using(var http=new HttpClient{Timeout=TimeSpan.FromSeconds(2)})
            for(var attempt=0;attempt<20&&!webReady;attempt++)
            {
                await Task.Delay(1000);
                foreach(var port in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(p=>System.Net.IPAddress.IsLoopback(p.Address)&&!existingPorts.Contains(p.Port)).Select(p=>p.Port).Distinct().Take(32))
                try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(2));using var response=await http.GetAsync("http://127.0.0.1:"+port,HttpCompletionOption.ResponseHeadersRead,deadline.Token);if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized){webReady=true;results["DeepSeek local endpoint requires browser authentication"]=true;results["DeepSeek local web endpoint"]="http://127.0.0.1:"+port;break;}if(!response.IsSuccessStatusCode)continue;await using var body=await response.Content.ReadAsStreamAsync();var bytes=new byte[262144];var size=0;int received;while(size<bytes.Length&&(received=await body.ReadAsync(bytes.AsMemory(size),deadline.Token))>0)size+=received;var text=System.Text.Encoding.UTF8.GetString(bytes,0,size);if(text.Contains("deepseek",StringComparison.OrdinalIgnoreCase)){webReady=true;results["DeepSeek local web endpoint"]="http://127.0.0.1:"+port;break;}}catch(Exception e) when(e is HttpRequestException or TaskCanceledException or IOException){}
            }
            results["DeepSeek interactive button starts protected local service"]=webReady&&window.LaunchResults.GetValueOrDefault("deepseek / Open interactive CLI");
            results["launch requests"]=starts;results["scope"]="actual desktop and interactive button dispatch, live process/window metadata and unauthenticated loopback HTTP probe; no browser UI, sign-in or generation claim";
        }
        catch(Exception e){results["error"]=e.ToString();}
        finally{window?.Close();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(!results.ContainsKey("error")&&results.Values.OfType<bool>().Count()>=5&&results.Values.OfType<bool>().All(v=>v)?0:1);}
    }
    internal static async Task Run(string output)
    {
        var results=new Dictionary<string,bool>();Window? window=null;
        try
        {
            var settings=new Settings{Models=new[]{"cursor","grok","codex"}.Select(engine=>new ModelProfile{Id=engine,Engine=engine,Executable=Environment.ProcessPath!,ModelPath=Environment.ProcessPath!}).ToList()};
            var fixture=Path.Combine(Path.GetTempPath(),"Infinity-tools-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var fixtureCli=Path.Combine(fixture,"dsh.cmd");File.WriteAllText(fixtureCli,"@exit /b 0");
            var starts=new List<ProcessStartInfo>();var tools=new AiToolsWindow(settings,starts.Add,()=>fixtureCli);window=tools;tools.Show();await Task.Delay(150);tools.UpdateLayout();
            ModelProfile? selected=null;var selection=new AiToolsWindow(settings,_=>throw new IOException("Selection must not launch an app"),()=>fixtureCli,p=>selected=p);selection.Show();selection.TestUseProvider("codex");results["useInstalledProviderInInfinityWithoutSendingOrLaunching"]=selected?.Engine=="codex"&&!selection.IsVisible;
            var blank=new Border{Width=40,Height=40};blank.Measure(new Size(40,40));blank.Arrange(new Rect(0,0,40,40));try{QaCapture.Save(blank,Path.Combine(fixture,"blank.png"));results["transparentQaCaptureCannotPass"]=false;}catch(IOException e){results["transparentQaCaptureCannotPass"]=e.Message.Contains("blank or transparent");}
            var whiteSurface=new Border{Width=100,Height=100,Background=Brushes.White};whiteSurface.Measure(new Size(100,100));whiteSurface.Arrange(new Rect(0,0,100,100));try{QaCapture.Save(whiteSurface,Path.Combine(fixture,"white.png"));results["whiteBackgroundQaCaptureCannotPass"]=false;}catch(IOException e){results["whiteBackgroundQaCaptureCannotPass"]=e.Message.Contains("white background");}
            foreach(var (width,height) in new[]{(1366,728),(1024,728)})
            {
                var dialogs=new Window[]{new AiToolsWindow(settings,starts.Add,()=>fixtureCli),new CodeSaveWindow("public class Layout {}","csharp")};
                var fits=true;
                foreach(var dialog in dialogs){dialog.Show();DarkTheme.ConstrainToWorkArea(dialog,new Rect(0,0,width,height));dialog.UpdateLayout();fits&=dialog.ActualWidth<=width&&dialog.ActualHeight<=height&&dialog.MinWidth<=width&&dialog.MinHeight<=height;Capture(dialog,Path.ChangeExtension(output,$".{dialog.GetType().Name}-{width}.png"));dialog.Close();}
                results[$"dialogsFit{width}x768WorkArea"]=fits;
            }
            results["fiveProvidersRendered"]=Children<TextBlock>(tools).Count(t=>t.FontSize==17)==5;
            results["deepSeekDesktopDiscoveryHasMissingFallback"]=AiLaunchers.DesktopStart("deepseek").FileName==(AiLaunchers.DesktopPath("deepseek")??AiLaunchers.DeepSeekDownloadStart().FileName);
            try{AiLaunchers.CliStart("deepseek","unused",null,()=>null);results["missingDeepSeekCliIsActionable"]=false;}catch(FileNotFoundException e){results["missingDeepSeekCliIsActionable"]=e.Message.Contains("official download");}
            results["officialWebsiteTarget"]=AiLaunchers.WebsiteStart("deepseek").FileName=="https://chat.deepseek.com";
            results["officialInstallTarget"]=AiLaunchers.DeepSeekDownloadStart().FileName=="https://www.deepseek.com/en/download/";
            foreach(var provider in new[]{"cursor","grok","codex","deepseek"})tools.TestLaunchButtons(provider);
            results["allTwelveClickHandlersBuildLaunchRequests"]=starts.Count==12&&tools.LaunchResults.Count==12&&tools.LaunchResults.Values.All(value=>value);
            var deepCli=starts.Last();
            results["deepSeekCliUsesInstalledToolWithoutScriptPolicyChange"]=deepCli.FileName==fixtureCli&&deepCli.UseShellExecute&&deepCli.ArgumentList.SequenceEqual(new[]{"web","--host","127.0.0.1","--port","0"});
            results["launchRequestsContainNoPromptOrSourceContext"]=starts.Select((request,index)=>new{request,index}).All(item=>
            {
                string[] expected=item.index switch{2=>[settings.Models.Single(p=>p.Engine=="cursor").ModelPath],6 when AiLaunchers.DesktopPath("codex")!=null=>[@"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App"],11=>["web","--host","127.0.0.1","--port","0"],_=>[]};
                return item.request.Arguments.Length==0&&item.request.ArgumentList.SequenceEqual(expected);
            });
            results["allInteractiveCliLaunchersAvoidPowerShell"]=new[]{2,5,8,11}.All(i=>starts[i].UseShellExecute&&!starts[i].FileName.Contains("powershell",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(starts[i].WorkingDirectory));
            results["deepSeekScopeExplained"]=Children<TextBlock>(tools).Any(t=>t.Text.Contains("Harness sign-in")&&t.Text.Contains("Each cloud request is reviewed")&&t.Text.Contains("text only"));
            results["buttonsHaveProviderAccessibleNames"]=Children<Button>(tools).Count(b=>System.Windows.Automation.AutomationProperties.GetName(b).Contains(": Open")&&!System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("GitHub Copilot:"))==12;
            Capture(tools,Path.ChangeExtension(output,".tools.png"));
            tools.Width=650;tools.Height=440;tools.UpdateLayout();((FrameworkElement)Children<TextBlock>(tools).First(t=>t.Text=="DeepSeek Harness").Parent).BringIntoView();await Task.Delay(100);tools.UpdateLayout();
            var buttons=Children<Button>(tools).Where(b=>System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("DeepSeek Harness:")).ToArray();
            results["deepSeekActionsReachableAtMinimumSize"]=buttons.Length==4&&buttons.All(b=>{var point=b.TranslatePoint(new Point(),tools);return point.X>=0&&point.X+b.ActualWidth<=tools.ActualWidth&&point.Y>=0&&point.Y+b.ActualHeight<=tools.ActualHeight;});
            Capture(tools,Path.ChangeExtension(output,".tools-narrow.png"));tools.Close();
            tools=new AiToolsWindow(settings,_=>throw new IOException("Synthetic launch failure"));window=tools;tools.Show();tools.TestLaunchButtons("deepseek");
            results["failedLaunchIsReportedWithoutSuccess"]=tools.LaunchResults.Count==3&&tools.LaunchResults.Values.All(value=>!value)&&Children<TextBlock>(tools).Any(t=>t.Text=="Cannot launch: Synthetic launch failure");tools.Close();
            if(Environment.GetEnvironmentVariable("LOCALAI_SANDBOX_NATIVE_PROBES")=="1")
            {
                var configured=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));
                foreach(var provider in new[]{"cursor","grok","codex","deepseek"})
                {
                    var profile=configured.Models.FirstOrDefault(p=>p.Engine==provider);
                    var info=AiLaunchers.CliStart(provider,Path.Combine(AppContext.BaseDirectory,"launch-provider.ps1"),profile);
                    if(provider=="deepseek")info.ArgumentList.Clear();info.ArgumentList.Add("--version");
                    using var process=Process.Start(info)??throw new IOException("CLI launch did not return a process: "+provider);
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
                    results["native CLI version launch succeeds without PowerShell - "+provider]=process.ExitCode==0;
                }
            }
        }
        catch(Exception e){results["unexpectedError"]=false;File.WriteAllText(Path.ChangeExtension(output,".error.txt"),e.ToString());window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(results.Values.All(value=>value)?0:1);
    }
}
