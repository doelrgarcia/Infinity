using System.IO;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    internal async Task ExerciseHybridChat(Dictionary<string,object> rows,ModelProfile profile)
    {
        model.SelectedItem=profile;maximum.Text="32";contextBudget.Text=profile.ContextLimit.ToString();conversation.AutoCliFallback=false;
        const string request="What is 2 plus 3? Reply with only 5.";
        await ExecuteRequest(profile,request,BuildMessages(request),true,false);
        rows["actualHybridChatUsesCompactPromptAndReplies"]=conversation.ActivityState=="Response ready"&&conversation.Messages.Last().Content.Trim()=="5";
        rows["hybridChatRemainsLocal"]=conversation.Messages.Where(m=>m.Provider!=null).All(m=>m.Provider==profile.Id);
        rows["hybridUsesVerifiedHelper"]=File.Exists(HybridBackend.DefaultHost);
        if(rows.Values.Any(v=>v is false)){rows["failureDetail"]=status.Text;rows["resolvedHelper"]=HybridBackend.DefaultHost;}
    }
}
internal static class HybridChatTests
{
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();MainWindow? window=null;string? log=null;
        try
        {
            var settings=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));var profile=settings.Models.Single(m=>m.Id=="llama32-hybrid");settings.Models=[profile];settings.GenerationTimeoutSeconds=120;
            window=new MainWindow(["--ui-session",Path.Combine(Path.GetDirectoryName(output)!,"hybrid-window.json")],settings);window.Show();await window.ExerciseHybridChat(rows,profile);
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{log=window?.ChatFileTestLog;window?.Close();}
        if(log!=null&&File.Exists(log)){File.Copy(log,Path.ChangeExtension(output,".log"),true);if(Path.GetDirectoryName(Path.GetFullPath(log))==Path.Combine(AppPaths.DataRoot,"logs"))File.Delete(log);}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));var exit=rows.Count>0&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
}
