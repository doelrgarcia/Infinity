using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class DependencySetupTests
{
    public static async Task RunNative(string output)
    {
        var results=new Dictionary<string,object>();
        try
        {
            if(!Environment.UserName.Equals("WDAGUtilityAccount",StringComparison.OrdinalIgnoreCase)||!File.Exists(@"C:\Results\guest-identity.txt"))throw new IOException("Native prerequisite QA is restricted to the disposable Windows Sandbox guest.");
            var config=Path.Combine(AppContext.BaseDirectory,"appsettings.json");var before=SHA256.HashData(File.ReadAllBytes(config));var settings=Settings.Load(config);using var setup=new StartupDependencies(settings);
            if(WindowsPrerequisites.VisualCppPresent)throw new IOException("This test requires the guest prerequisite to be absent before user selection.");
            await setup.CheckTools(default);var inspection=setup.Inspect();
            results["only Visual C++ prerequisite missing"]=inspection.Missing.Select(p=>p.Id).SequenceEqual(new[]{WindowsPrerequisites.VisualCppId});
            if(results["only Visual C++ prerequisite missing"] is not true)throw new IOException("Unexpected missing dependencies.");
            var window=new DependencySetupWindow(settings,setup);
            window.Loaded+=async(_,_)=>
            {
                try
                {
                    while(window.TestBusy)await Task.Delay(20);
                    results["native install waits for selection"]=window.TestSelections==0&&!setup.Installer.IsInstalled(setup.Catalog.Packages.Single(p=>p.Id==WindowsPrerequisites.VisualCppId));
                    Capture(window,Path.ChangeExtension(output,".before.png"));window.TestSelect(WindowsPrerequisites.VisualCppId);await window.TestInstall();
                    var ready=setup.Inspect();results["native runtime installed through setup UI"]=WindowsPrerequisites.VisualCppPresent;
                    results["all six profiles available after native install"]=ready.ReadyProfiles.Count==6&&ready.Missing.Count==0;
                    results["no prerequisite notices after successful fresh install"]=ready.Notices.Count==0;
                    Capture(window,Path.ChangeExtension(output,".after.png"));Click(window,"Continue with available tools");
                }
                catch(Exception e){results["error"]=e.ToString();window.Close();}
            };
            results["continue accepted after real native installation"]=window.ShowDialog()==true;
            results["native setup preserves configuration"]=before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(config)));
        }
        catch(Exception e){results["error"]=e.ToString();}
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(results.Values.Any(v=>v is false)||results.ContainsKey("error")?1:0);
    }
    private sealed class Handler(byte[] bytes):HttpMessageHandler
    {
        public int Calls;public bool Fail;public bool Slow;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;if(Slow)await Task.Delay(10000,token);return new(Fail?HttpStatusCode.Forbidden:HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};}
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Children<T>(child))yield return nested;}}
    private static void Click(Window w,string content)=>Children<Button>(w).Single(b=>Equals(b.Content,content)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static void Capture(Window w,string path)=>QaCapture.Save(w,path);
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output)!);var results=new Dictionary<string,object>();
        var root=Path.Combine(Path.GetTempPath(),"Infinity-setup-ui-"+Guid.NewGuid().ToString("N"));var bytes=new byte[4096];"GGUF"u8.CopyTo(bytes);var hash=Convert.ToHexString(SHA256.HashData(bytes));
        DependencyPackage Package(string id)=>new(){Id=id,Label="Synthetic "+id,Version="1",InstalledBytes=bytes.Length,LicenseUrl="https://huggingface.co/",RequiredFiles=["model.gguf"],Artifacts=[new(){Url="https://huggingface.co/fixture/resolve/pinned/model.gguf",Sha256=hash,Bytes=bytes.Length,Path="model.gguf"}]};
        var first=Package("first");var second=Package("second");var settings=new Settings{Models=[new(){Id="first",Executable="missing-first",ModelPath="missing-first"},new(){Id="second",Executable="missing-second",ModelPath="missing-second"}]};var original=JsonSerializer.Serialize(settings);
        var catalog=new DependencyCatalog{Packages=[first,second],Bindings=[new(){ProfileId="first",ExecutablePackage="first",ExecutablePath="model.gguf",ModelPackage="first",ModelPath="model.gguf"},new(){ProfileId="second",ExecutablePackage="second",ExecutablePath="model.gguf",ModelPackage="second",ModelPath="model.gguf"}]};var handler=new Handler(bytes);using var setup=new StartupDependencies(settings,root,catalog,handler);var window=new DependencySetupWindow(settings,setup);
        void Check(string name,bool value){results[name]=value;if(!value)throw new IOException("Assertion failed: "+name);}
        window.Loaded+=async(_,_)=>
        {
            try
            {
                while(window.TestBusy)await Task.Delay(20);
                Check("startup asks before downloading",handler.Calls==0&&window.TestSelections==0&&!window.TestContinueEnabled);Check("inspection preserves configured values",JsonSerializer.Serialize(settings)==original);Capture(window,Path.ChangeExtension(output,".missing.png"));window.Width=650;window.Height=430;window.UpdateLayout();Capture(window,Path.ChangeExtension(output,".missing-narrow.png"));window.Width=900;window.Height=690;
                UiPathTests.Click(Children<CheckBox>(window).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Install Synthetic first"));handler.Fail=true;await window.TestInstall();Check("publisher failure shown with retry",window.TestStatus.Contains("requires account/license access")&&!window.TestContinueEnabled&&setup.Inspect().Missing.Count==2);Capture(window,Path.ChangeExtension(output,".failure.png"));
                handler.Fail=false;handler.Slow=true;var running=window.TestInstall();await Task.Delay(80);Check("progress disables continue while installing",window.TestBusy&&!window.TestContinueEnabled);Capture(window,Path.ChangeExtension(output,".progress.png"));Click(window,"Cancel download");await running;Check("cancel retains missing package and retry choice",window.TestStatus.Contains("cancelled")&&setup.Inspect().Missing.Count==2&&!window.TestBusy);
                handler.Slow=false;await window.TestInstall();Check("installs only selected package",setup.Inspect().ReadyProfiles.SequenceEqual(new[]{"first"})&&setup.Inspect().Missing.Single().Id=="second");Check("continue enabled with available profile",window.TestContinueEnabled);Check("resolved profile points to managed file",File.Exists(settings.Models[0].ModelPath)&&settings.Models[1].ModelPath=="missing-second");Capture(window,Path.ChangeExtension(output,".partial-ready.png"));
                Click(window,"Recheck");while(window.TestBusy)await Task.Delay(20);Check("recheck does not download",setup.Inspect().Missing.Count==1);Click(window,"Select all missing");var secondChoice=Children<CheckBox>(window).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Install Synthetic second");UiPathTests.Click(secondChoice);UiPathTests.Click(secondChoice);Check("select all checks missing package",window.TestSelections==1);await window.TestInstall();Check("all selected profiles available",setup.Inspect().ReadyProfiles.Count==2&&setup.Inspect().Missing.Count==0);Capture(window,Path.ChangeExtension(output,".ready.png"));
                window.Width=650;window.Height=430;window.UpdateLayout();Capture(window,Path.ChangeExtension(output,".narrow.png"));var button=Children<Button>(window).Single(b=>Equals(b.Content,"Continue with available tools"));var location=button.TranslatePoint(new Point(),window);Check("continue visible at minimum size",location.Y>=0&&location.Y+button.ActualHeight<=window.ActualHeight);Click(window,"Continue with available tools");
            }
            catch(Exception e){results["error"]=e.ToString();window.Close();}
        };
        try{results["continue button returns accepted dialog"]=window.ShowDialog()==true;}catch(Exception e){results["error"]=e.ToString();}
        if(!results.ContainsKey("error"))
        {
            var repairing=new DependencySetupWindow(settings,setup,true);repairing.Loaded+=async(_,_)=>{try{while(repairing.TestBusy)await Task.Delay(20);Check("repair is available with all packages installed",repairing.TestSelections==0&&repairing.TestContinueEnabled);Click(repairing,"Show missing only");Check("repair toggle returns missing-only view",Children<Button>(repairing).Any(b=>Equals(b.Content,"Repair managed downloads"))&&repairing.TestSelections==0);Click(repairing,"Repair managed downloads");Check("repair toggle exposes listed packages",Children<Button>(repairing).Any(b=>Equals(b.Content,"Select all listed")));Click(repairing,"Select all listed");Check("select all listed checks both synthetic packages",repairing.TestSelections==2);var payload=settings.Models[0].ModelPath;File.WriteAllBytes(payload,new byte[bytes.Length]);var callsBefore=handler.Calls;repairing.TestSelect("first");await repairing.TestInstall();Check("repair UI restores same-length corrupt managed package",handler.Calls==callsBefore+1&&File.ReadAllBytes(payload).SequenceEqual(bytes));DarkTheme.ConstrainToWorkArea(repairing,new Rect(0,0,800,500));repairing.UpdateLayout();Check("dialog dimensions and minimums fit small work area",repairing.Width<=800&&repairing.Height<=500&&repairing.MinHeight<=500&&repairing.MaxHeight<=500);Capture(repairing,Path.ChangeExtension(output,".repair.png"));Click(repairing,"Continue with available tools");}catch(Exception e){results["error"]=e.ToString();repairing.Close();}};repairing.ShowDialog();
            var calls=handler.Calls;var alreadyReady=new DependencySetupWindow(settings,setup);results["all present startup continues without download or prompt"]=alreadyReady.ShowDialog()==true&&handler.Calls==calls;
            var tools=new AiToolsWindow(settings);tools.Loaded+=(_,_)=>{try{Click(tools,"Check tools & models");Check("tools panel reopens dependency check",Children<TextBlock>(tools).Any(t=>t.Text.Contains("Tools checked.")));Capture(tools,Path.ChangeExtension(output,".tools.png"));tools.Width=650;tools.Height=440;tools.UpdateLayout();Capture(tools,Path.ChangeExtension(output,".tools-narrow.png"));tools.Close();}catch(Exception e){results["error"]=e.ToString();tools.Close();}};tools.ShowDialog();
            var third=Package("third");catalog.Packages.Add(third);catalog.Bindings.Add(new(){ProfileId="third",ExecutablePackage="third",ExecutablePath="model.gguf",ModelPackage="third",ModelPath="model.gguf"});settings.Models.Add(new(){Id="third",Executable="missing-third",ModelPath="missing-third"});var closing=new DependencySetupWindow(settings,setup);handler.Slow=true;
            Task? closingTask=null;async Task CloseTest(){try{while(closing.TestBusy)await Task.Delay(20);UiPathTests.Click(Children<CheckBox>(closing).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Install Synthetic third"));var pending=closing.TestInstall();await Task.Delay(80);closing.Close();await pending;results["close during download cancels without installing"]=!setup.Installer.IsInstalled(third)&&!closing.TestBusy;}catch(Exception e){results["error"]=e.ToString();closing.Close();}}
            closing.Loaded+=(_,_)=>closingTask=CloseTest();results["close during download returns cancelled dialog"]=closing.ShowDialog()==false;if(closingTask!=null)await closingTask;
        }
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(results.Count>0&&results.Values.All(v=>v is bool passed&&passed)?0:1);
    }
}
