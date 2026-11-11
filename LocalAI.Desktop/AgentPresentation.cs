using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalAI.Core;
namespace LocalAI.Desktop;
internal static class AgentPresentation
{
    internal static bool VisibleReview(AgentApproval approval)=>approval.Status is "Pending review" or "Reviewing"||approval.Status.StartsWith("Needs attention",StringComparison.Ordinal)||approval.Status.StartsWith("Outcome unknown",StringComparison.Ordinal)||approval.Status.StartsWith("Partially applied",StringComparison.Ordinal);
    internal static string TaskLabel(AgentTask task)=>task.Title+"\n"+(task.Status=="Response ready"?"Reply received · completion unverified":task.Status=="Verified complete"&&task.VerifiedAt==null?"Needs verification":task.Status);
    internal static string ApprovalLabel(AgentApproval approval)
    {
        var status=approval.Status=="Pending review"?"Needs review":approval.Status;
        if(approval.Kind=="files")
        {
            try
            {
                var actions=FileActionProtocol.Parse(approval.Value);
                string Name(FileAction a){var name=Path.GetFileName(a.Path.TrimEnd('\\','/'));return new string(name.Take(90).Select(c=>char.IsControl(c)?' ':c).ToArray());}
                var caption=actions.Count==1?(actions[0].Operation=="mkdir"?"Create folder: ":actions[0].Operation+": ")+Name(actions[0]):actions.All(a=>a.Operation=="mkdir")?$"Create {actions.Count} folders: "+string.Join(", ",actions.Take(3).Select(Name))+(actions.Count>3?", …":""):$"{actions.Count} file actions: "+string.Join(", ",actions.Take(2).Select(Name));
                return caption+"\n"+status;
            }
            catch(Exception e) when(e is IOException or System.Text.Json.JsonException or ArgumentException){return "Invalid file proposal\n"+status;}
        }
        return (approval.Kind=="command"?(approval.Purpose is "build" or "test"?approval.Purpose+" command":"Command review"):approval.Kind=="memory"?"Agent memory":approval.Kind=="project"?"Project notes":approval.Kind=="browser"?"Browser action":approval.Kind)+"\n"+status;
    }
    internal static DataTemplate Template(Func<object,string> caption)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding{Converter=new CaptionConverter(caption)});text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(FrameworkElement.MarginProperty,new Thickness(3,5,3,5));return new DataTemplate{VisualTree=text};
    }
    private sealed class CaptionConverter(Func<object,string> caption):IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>caption(value);
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
