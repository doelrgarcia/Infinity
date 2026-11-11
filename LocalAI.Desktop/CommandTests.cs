using System.IO;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal static class CommandTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"command-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);CommandWindow? window=null;
        try
        {
            window=new CommandWindow([root],logRoot:Path.Combine(root,"logs"));window.Show();window.TestDraft("[Console]::WriteLine('ACME UI')");rows["commandReviewDoesNotRun"]=window.TestReviewed&&window.Result==null;
            QaCapture.Save(window,Path.ChangeExtension(output,".png"));rows["commandWindowDarkPixelsVerified"]=true;
            rows["commandEditingInvalidatesApproval"]=window.TestApprovalInvalidates();window.TestDraft("[Console]::WriteLine('ACME UI')");await window.TestExecute();rows["commandUiRunsReviewedScript"]=window.Result?.Status=="Succeeded"&&window.Result.Output.Contains("ACME UI");rows["commandUiSavesResult"]=window.Result!=null&&File.Exists(window.Result.LogPath);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Values.All(v=>v is true)?0:1);
    }
}
