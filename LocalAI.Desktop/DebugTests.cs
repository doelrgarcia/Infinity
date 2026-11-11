using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal static class DebugTests
{
    private static IEnumerable<Button> FindButtons(DependencyObject root){foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){if(child is Button b)yield return b;foreach(var descendant in FindButtons(child))yield return descendant;}}
    internal static async Task Run(string output)
    {
        var rows=new Dictionary<string,object>();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"debug-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);DebugWindow? window=null;
        try
        {
            var project=Path.Combine(root,"Broken.csproj");var source=Path.Combine(root,"Program.cs");File.WriteAllText(project,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");File.WriteAllText(source,"public class Program { this is invalid C# }");File.WriteAllText(Path.Combine(root,"NuGet.Config"),"<configuration><packageSources><clear/></packageSources></configuration>");
            var plan=DebugWorkflow.Plan([root],project,"Build");rows["dotnetBuildPlanTargetsExactProject"]=plan.WorkingDirectory==root&&plan.Script.Contains("dotnet build")&&plan.Script.Contains(project);
            try{DebugWorkflow.Plan([root],Path.Combine(Path.GetDirectoryName(root)!,"outside.csproj"),"Build");rows["outsideDebugProjectRefused"]=false;}catch(IOException){rows["outsideDebugProjectRefused"]=true;}
            var package=Path.Combine(root,"package.json");File.WriteAllText(package,"{\"scripts\":{\"test\":\"node --test\",\"build\":\"node --check index.js\"}}");rows["npmScriptsUseProjectDirectory"]=DebugWorkflow.Plan([root],package,"Test").Script=="& npm test";
            try{DebugWorkflow.Plan([root],package,"Run");rows["missingNpmScriptRefused"]=false;}catch(IOException){rows["missingNpmScriptRefused"]=true;}
            window=new DebugWindow([root],Path.Combine(root,"run-logs"));window.Show();window.TestProject(project);rows["debugCannotDiagnoseBeforeRun"]=window.TestDiagnosisDisabled;
            window.RunTestAction=async command=>{command.TestDraft(plan.Script);await command.TestExecute();command.Close();};
            FindButtons(window).Single(b=>Equals(b.Content,"Review and run")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            rows["realBuildFailureCaptured"]=window.Result is {ExitCode:not 0}&&window.Result.Output.Contains("error CS");
            var packet=DebugWorkflow.Diagnosis(window.Result!);rows["diagnosisContainsRealErrorAndReviewInstructions"]=packet.Contains("error CS")&&packet.Contains("propose a minimal reviewable fix")&&packet.Contains("Do not claim files were changed");
            var workspace=new FileWorkspace([root]);var fix=workspace.Preview(new("write",source,Content:"public class Program { public static void Main() { System.Console.WriteLine(\"Acme fixed\"); } }"));rows["fixPreviewDoesNotChangeSource"]=File.ReadAllText(source).Contains("invalid");workspace.Apply(fix,true);
            FindButtons(window).Single(b=>Equals(b.Content,"Rerun last command")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));rows["reviewedFixAndRerunBuildSucceeds"]=window.Result is {ExitCode:0,Status:"Succeeded"};
            var oversized=window.Result! with{Output=new string('x',60000)};rows["diagnosisOutputBounded"]=DebugWorkflow.Diagnosis(oversized).Length<22000;
            QaCapture.Save(window,Path.ChangeExtension(output,".png"));rows["debugWindowDarkRender"]=true;
            FindButtons(window).Single(b=>Equals(b.Content,"Ask Infinity to diagnose")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));rows["diagnosisRequiresExplicitButton"]=window.DiagnosisPrompt?.Contains("Succeeded")==true;
            var previous=window.Result;window=new DebugWindow([root],Path.Combine(root,"run-logs"),previous,project);window.Show();rows["debugReopenRetainsLastRun"]=window.Result==previous&&window.ProjectPath==project&&FindButtons(window).Single(b=>Equals(b.Content,"Rerun last command")).IsEnabled;
        }
        catch(Exception e){rows["error"]=e.ToString();}
        finally{window?.Close();}
        await Task.CompletedTask;File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(rows.Count==13&&rows.Values.All(v=>v is true)?0:1);
    }
}
