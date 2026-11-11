using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    // Explicit, synthetic, approved live test; never included in the offline release gate.
    internal async Task ExerciseCopilotLive(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"fixture");Directory.CreateDirectory(root);
        var source=Path.Combine(root,"Program.cs");const string broken="System.Console.WriteLine(Add(2,3)); static int Add(int a,int b)=>a-b;";
        File.WriteAllText(source,broken);File.WriteAllText(Path.Combine(root,"Fixture.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");File.WriteAllText(Path.Combine(root,"NuGet.Config"),"<configuration><packageSources><clear /></packageSources></configuration>");
        var originalProject=File.ReadAllText(Path.Combine(root,"Fixture.csproj"));
        const string build="dotnet build './Fixture.csproj' -c Release --nologo";
        const string check="$value = & dotnet run --project './Fixture.csproj' -c Release --no-build; if ($LASTEXITCODE -ne 0 -or ($value -join '').Trim() -ne '5') { throw 'Expected 5' }; Write-Output 'ASSERTION_PASSED'";
        var profile=settings.Models.Single(p=>p.Engine=="copilot");conversation.WorkspaceRoots=[root];conversation.AutoCliFallback=false;model.SelectedItem=profile;maximum.Text="2048";contextBudget.Text="131072";
        var reviewed=new List<IReadOnlyList<Message>>();var reviews=0;var rejected=false;
        chatCommandReviewTest=async window=>
        {
            var request=window.TestRequest;
            // Never execute arbitrary model code even in an approved synthetic test.
            var safeSource=File.ReadAllText(source).Trim()==broken.Replace("a-b","a+b");
            if(!safeSource||File.ReadAllText(Path.Combine(root,"Fixture.csproj"))!=originalProject||File.ReadAllText(Path.Combine(root,"NuGet.Config"))!="<configuration><packageSources><clear /></packageSources></configuration>"||request.WorkingDirectory!=root||request.Script!=build&&request.Script!=check){rejected=true;window.Close();return;}
            reviews++;await window.TestExecuteWithCurrentDraft();if(window.Result==null){rejected=true;window.Close();}
        };
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        timer.Tick+=(_,_)=>
        {
            foreach(var consent in Application.Current.Windows.OfType<ProviderConsent>().ToArray())
            {
                if(reviewed.Count>=4){rejected=true;consent.Close();active?.Cancel();return;}
                reviewed.Add(consent.ReviewedMessages);File.WriteAllText(Path.ChangeExtension(output,".reviewed-requests.json"),JsonSerializer.Serialize(reviewed));consent.TestConfirm();
            }
        };
        try
        {
            timer.Start();
            var request=$"Fix and test this synthetic calculator. The entire Program.cs is: {broken}\nIts absolute path is {JsonSerializer.Serialize(source)}. First return a single infinity-files write action to fix Add, preserving the rest. Then request exactly this build command: {build}. Then exactly this test command: {check}. Use workingDirectory {JsonSerializer.Serialize(root)} and timeoutSeconds 120. Return only one tool block per turn. Do not read other files, change Fixture.csproj, or run other commands. After actual receipts give a concise result.";
            await ExecuteRequest(profile,request,BuildMessages(request),true,false);
            var runs=conversation.Messages.Where(m=>m.Content.StartsWith(ChatCommands.ResultPrefix)).Select(m=>JsonSerializer.Deserialize<CommandRun>(m.Content[ChatCommands.ResultPrefix.Length..])).Where(r=>r!=null).ToArray();
            rows["actualSourceWasFixedWithBackup"]=File.ReadAllText(source)!=broken&&Directory.GetFiles(Path.Combine(root,".localai-backups"),"*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)==broken);
            rows["actualBuildPassed"]=runs.Any(r=>r!.Request.Script==build&&r.Status=="Succeeded"&&r.ExitCode==0);
            rows["actualFunctionalAssertionPassed"]=runs.Any(r=>r!.Request.Script==check&&r.Status=="Succeeded"&&r.Output.Contains("ASSERTION_PASSED"));
            rows["everyExecutedCommandReviewed"]=reviews==runs.Length&&reviews>=2&&!rejected;
            rows["projectAndOtherProvidersPreserved"]=File.ReadAllText(Path.Combine(root,"Fixture.csproj"))==originalProject&&conversation.Messages.Where(m=>m.Role=="assistant").All(m=>m.Provider==profile.Id);
            rows["withinApprovedRequestBudget"]=reviewed.Count<=4;rows["requests"]=reviewed.Count;rows["responseReady"]=conversation.ActivityState=="Response ready";
            if(rows.Values.OfType<bool>().Any(v=>!v))rows["status"]=status.Text;
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{timer.Stop();active?.Cancel();File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Close();Application.Current.Shutdown(rows.Count>0&&!rows.ContainsKey("error")&&rows.Values.OfType<bool>().All(v=>v)?0:1);}
    }
}
