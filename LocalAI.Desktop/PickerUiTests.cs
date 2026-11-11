using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LocalAI.Desktop;

internal static class PickerUiTests
{
    internal static void Run(string output)
    {
        var rows=new Dictionary<string,object>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"picker-fixture-"+Guid.NewGuid().ToString("N"));
        var first=Path.Combine(root,"First folder");var second=Path.Combine(root,"Second Ω folder");Directory.CreateDirectory(first);Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first,"First.cs"),"public class First {}");File.WriteAllText(Path.Combine(second,"Second.cs"),"public class Second {}");File.WriteAllText(Path.Combine(second,"Another.cs"),"public class Another {}");File.WriteAllText(Path.Combine(second,"appsettings.json"),"{}");
        void Dialog(SourcePicker picker,Action<SourcePicker> action)
        {
            picker.Loaded+=(_,_)=>{try{action(picker);}catch(Exception error){rows["error"]=error.ToString();}finally{if(picker.IsVisible)picker.Close();}};
            picker.ShowDialog();
        }
        try
        {
            var edited=new SourcePicker(true,first);
            Dialog(edited,p=>{p.TestSetAddress("  "+second+"  ");p.TestConfirm();rows["folderConfirmUsesEditedAddressWithoutOpenPath"]=!p.IsVisible&&p.SelectedPaths.SequenceEqual(new[]{second});});
            Dialog(new(true,first),p=>{p.TestSetAddress(Path.Combine(root,"missing"));p.TestConfirm();rows["invalidAddressCannotReturnPreviousFolder"]=p.IsVisible&&p.SelectedPaths.Length==0&&p.TestStatus.Contains("missing");rows["pickerErrorExposesActualMessage"]=p.TestAccessibleStatus==p.TestStatus;p.UpdateLayout();QaCapture.Save(p,Path.ChangeExtension(output,".invalid-address.png"));});
            var cancelled=new SourcePicker(true,first);Dialog(cancelled,_=>{});rows["folderCancellationDoesNotReturnASelection"]=cancelled.SelectedPaths.Length==0;
            Dialog(new(false,first),p=>{p.TestChoose("First.cs");rows["sourceSelectionSurvivesSameFolderRefresh"]=!p.IsVisible&&p.SelectedPaths.SequenceEqual(new[]{Path.Combine(first,"First.cs")});});
            Dialog(new(false,first),p=>
            {
                p.TestSelectFiles("First.cs");p.TestSetAddress(second);p.TestConfirm();
                rows["sourceAddressChangeDropsOldSelection"]=p.IsVisible&&p.SelectedPaths.Length==0&&p.TestVisibleFiles.Contains("Second.cs");
                rows["configurationStillExcludedAfterAddressChange"]=!p.TestVisibleFiles.Contains("appsettings.json");
                p.TestChoose("Second.cs","Another.cs");rows["multipleFilesSelectedFromEditedFolder"]=p.SelectedPaths.Length==2&&p.SelectedPaths.All(f=>Path.GetDirectoryName(f)==second);
            });
            Dialog(new(false,first),p=>{p.TestConfirm();rows["emptyFileSelectionShowsActionableError"]=p.IsVisible&&p.SelectedPaths.Length==0&&p.TestStatus=="Select one or more source files.";});
            Dialog(new(true,first),p=>
            {
                rows["folderModeInstructionsMatchFolderSelection"]=p.TestStatus.Contains("Include this folder")&&!p.TestStatus.Contains("Ctrl");
                var buttons=Descendants<Button>(p).Where(b=>b.Content is not string).ToArray();
                rows["pickerButtonsAndPathHaveAccessibleNames"]=buttons.All(b=>!string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)))&&Descendants<TextBox>(p).Any(t=>AutomationProperties.GetName(t)=="Folder path");
                rows["pickerNavigationButtonsHandleSpacedUnicodeFolders"]=p.TestNavigate(second);
            });
            var project=new NewProjectWindow();project.Loaded+=(_,_)=>
            {
                try
                {
                    project.TestSubmit("");var error=Descendants<TextBlock>(project).Single(t=>AutomationProperties.GetAutomationId(t)=="ProjectCreationError");
                    rows["projectValidationExposesActualMessage"]=project.IsVisible&&project.TestError.Contains("Enter a project name")&&AutomationProperties.GetName(error)==project.TestError;
                    project.UpdateLayout();QaCapture.Save(project,Path.ChangeExtension(output,".project-error.png"));
                }
                finally{project.Close();}
            };project.ShowDialog();
        }
        catch(Exception error){rows["error"]=error.ToString();}
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));var exit=rows.Count==13&&rows.Values.All(v=>v is true)?0:1;Environment.ExitCode=exit;Application.Current.Shutdown(exit);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        var seen=new HashSet<DependencyObject>();var pending=new Stack<DependencyObject>();pending.Push(root);
        while(pending.TryPop(out var item)){if(!seen.Add(item))continue;if(item is T match)yield return match;if(item is Visual)for(var i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)pending.Push(VisualTreeHelper.GetChild(item,i));if(item is FrameworkElement)foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())pending.Push(child);}
    }
}
