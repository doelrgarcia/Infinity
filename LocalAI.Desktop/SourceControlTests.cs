using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class SourceControlTests
{
    internal static async Task Run(string output)
    {
        var results=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"source-ui-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var git=SourceControl.FindTool(SourceProvider.Git)??throw new IOException("Git unavailable for isolated source UI tests.");
        async Task Git(params string[] args){var info=new ProcessStartInfo(git){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);using var p=Process.Start(info)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await stdout;var error=await stderr;if(p.ExitCode!=0)throw new IOException(error);}
        SourceControlWindow? window=null;
        try
        {
            await Git("init","-b","main");var file=Path.Combine(root,"Example.txt");File.WriteAllText(file,"Synthetic UI draft\n");
            var launches=new List<ProcessStartInfo>();string? copied=null;
            window=new SourceControlWindow([root],root,file,info=>launches.Add(info),text=>copied=text);window.Show();await window.TestRead();results["sourceWindowReadsRealGitStatus"]=window.TestOutput.Contains("Example.txt");
            bool Accessible(string name)
            {
                var pending=new Queue<AutomationPeer>();pending.Enqueue(UIElementAutomationPeer.CreatePeerForElement(window));
                while(pending.TryDequeue(out var peer)){if(peer.GetName()==name)return true;peer.ResetChildrenCache();foreach(var child in peer.GetChildren()??[])pending.Enqueue(child);}return false;
            }
            results["sourceTabExposesEditableFieldsToAccessibility"]=Accessible("Source control executable")&&Accessible("Commit message");
            await window.TestPreview(file);results["sourceWindowPreviewDoesNotStage"]=window.TestCanApply&&!File.Exists(Path.Combine(root,".git","index"));
            QaCapture.Save(window,Path.ChangeExtension(output,".source.png"));results["sourceWindowDarkPixelsVerified"]=true;
            await window.TestApply();results["sourceWindowConfirmsSelectedStage"]=File.Exists(Path.Combine(root,".git","index"))&&!window.TestCanApply;
            window.TestTeamsTab();window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);QaCapture.Save(window,Path.ChangeExtension(output,".teams.png"));results["teamsTabDarkPixelsVerified"]=true;
            results["teamsTabExposesDraftToAccessibility"]=Accessible("Reviewed Teams draft");
            window.TestTeamDraft(false);results["teamsButtonRequiresExplicitDraftConsent"]=launches.Count==0&&window.TestTeamsNoteVisible;
            window.TestTeamDraft(true);results["teamsButtonDispatchesOnlyReviewedDraft"]=launches.Count==1&&launches[0].FileName.StartsWith("https://teams.microsoft.com/l/chat/0/0?")&&Uri.UnescapeDataString(launches[0].FileName).Contains("Synthetic reviewed update");
            results["teamsEditingInvalidatesConsent"]=window.TestDraftConsentResets();window.TestClick("Copy draft");results["teamsCopyButtonUsesOnlyDraft"]=copied=="Synthetic reviewed update changed";
            window.TestClick("Open Teams desktop / web");results["teamsHomeDispatchUsesOfficialDesktopOrWeb"]=launches.Count==2&&launches[1].FileName is "msteams:" or "https://teams.microsoft.com";
            DarkTheme.ConstrainToWorkArea(window,new Rect(0,0,1366,728));window.UpdateLayout();results["sourceWindowFits1366WorkArea"]=window.Width<=1366&&window.Height<=728;
            QaCapture.Save(window,Path.ChangeExtension(output,".1366.png"));
        }
        catch(Exception e){results["error"]=e.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));var exit=results.Count>0&&results.Values.All(v=>v is true)?0:1;Application.Current.Shutdown(exit);
    }
}
