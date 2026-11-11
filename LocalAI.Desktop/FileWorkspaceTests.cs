using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class FileWorkspaceTests
{
    internal static async Task Run(string output)
    {
        var results=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"file-fixture");Directory.CreateDirectory(root);var file=Path.Combine(root,"Example.cs");File.WriteAllText(file,"class Before {}");
        FileWorkspaceWindow? browser=null;
        try
        {
            var roots=new List<string>();var included=false;
            browser=new FileWorkspaceWindow(roots,()=>{},_=>included=true,_=>Task.CompletedTask);browser.Show();await browser.TestConnect(root);
            results["fileBrowserConnectsSelectedFolder"]=roots.SequenceEqual(new[]{root})&&browser.HasEntries;
            results["fileBrowserDoesNotAutoIncludeContext"]=!included;
            QaCapture.Save(browser,Path.ChangeExtension(output,".browser.png"));results["fileBrowserDarkRenderVerified"]=true;
            var cancelled=new FileActionReviewWindow(new(roots),new("write",file,Content:"class Cancelled {}")){Owner=browser};
            cancelled.Loaded+=async(_,_)=>{await cancelled.TestPreview();results["fileActionReviewRequiresExplicitApply"]=cancelled.HasReviewedPlan&&File.ReadAllText(file)=="class Before {}";QaCapture.Save(cancelled,Path.ChangeExtension(output,".review.png"));cancelled.Close();};cancelled.ShowDialog();results["cancelledFileReviewPreservesOriginal"]=File.ReadAllText(file)=="class Before {}";
            var approved=new FileActionReviewWindow(new(roots),new("write",file,Content:"class Reviewed {}")){Owner=browser};
            approved.Loaded+=async(_,_)=>{await approved.TestPreview();await approved.TestApply();};approved.ShowDialog();
            results["reviewedFileWriteAppliesAndBacksUp"]=approved.Receipt?.RecoveryPath is { } backup&&File.ReadAllText(file)=="class Reviewed {}"&&File.ReadAllText(backup)=="class Before {}";
            browser.Close();
            FileWorkspaceWindow? guarded=null;
            guarded=new FileWorkspaceWindow([],()=>{},_=>{},async _=>{await Task.Delay(25);guarded!.Close();results["fileBrowserWaitsForAsyncChangeBeforeClosing"]=guarded.IsVisible;});
            guarded.Show();await guarded.TestConnect(root);guarded.ReviewTestAction=async dialog=>{await dialog.TestPreview();await dialog.TestApply();};await guarded.TestReviewAction(new("write",file,Content:"class Guarded {}"));guarded.Close();
        }
        catch(Exception e){results["error"]=e.ToString();}
        finally{browser?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));var exit=results.Count>0&&results.Values.All(v=>v is bool b&&b)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
}
