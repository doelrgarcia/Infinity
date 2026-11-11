using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        string[] allowed=["--copilot-qa","--drop-ui-qa","--ui-auxiliary-qa","--ui-agent-paths-qa","--ui-paths-qa","--ui-dialogs-qa","--ui-files-qa","--picker-ui-qa","--tools-qa","--setup-qa","--workspace-qa","--escalation-qa","--chat-files-qa","--chat-commands-qa","--project-qa","--agent-features-qa","--agent-ui-qa","--agent-qa","--chat-assistant-qa","--debug-qa","--browser-qa","--app-controls-qa","--voice-ui-qa","--commands-ui-qa","--source-ui-qa","--files-ui-qa"];
        if(args.Length!=3||!allowed.Contains(args[1]))throw new ArgumentException("Supply installed app folder, an offline UI suite and an owned temporary result path.");
        var target=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);var temporary=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if(!output.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("UI coverage output must stay inside this process's temporary root.");
        if(Path.GetFullPath(AppContext.BaseDirectory).Equals(target,StringComparison.OrdinalIgnoreCase))throw new IOException("The collector must run separately from the installed application.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        AssemblyLoadContext.Default.Resolving+=(_,name)=>
        {
            var relative=string.IsNullOrEmpty(name.CultureName)?name.Name+".dll":Path.Combine(name.CultureName,name.Name+".dll");
            var file=Path.GetFullPath(Path.Combine(target,relative));
            return file.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(file)?AssemblyLoadContext.Default.LoadFromAssemblyPath(file):null;
        };
        var collector=new Collector(args[1],output);collector.Start();
        // This is an in-process WPF test collector, not Windows UI Automation.
        // It observes the installed application's existing offline tests; it
        // never invents clicks, confirms reviews or dispatches provider requests.
        try
        {
            var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(target,"LocalAI.Desktop.dll"));
            var required=args[1] switch {"--drop-ui-qa"=>"AttachmentDropTests","--ui-auxiliary-qa"=>"UiAuxiliaryPathTests","--ui-agent-paths-qa"=>"UiAgentPathTests","--picker-ui-qa"=>"PickerUiTests","--ui-paths-qa"=>"UiPathTests","--ui-dialogs-qa"=>"UiDialogPathTests","--ui-files-qa"=>"UiFilePathTests",_=>null};
            if(required!=null&&assembly.GetType("LocalAI.Desktop."+required)==null)throw new IOException("The selected application does not include the requested UI regression suite.");
            assembly.EntryPoint!.Invoke(null,[new[]{args[1],output}]);
        }
        catch(Exception error){collector.Error(error is TargetInvocationException {InnerException:not null} wrapped?wrapped.InnerException!:error);Environment.ExitCode=1;}
        finally{collector.Save();}
        return Environment.ExitCode;
    }
}

internal sealed class Collector(string suite,string output)
{
    private sealed class Row
    {
        public string Key {get;set;}="";public string Window {get;set;}="";public string Type {get;set;}="";public string Label {get;set;}="";
        public bool EverVisible {get;set;}public bool EverEnabled {get;set;}public bool EverDisabled {get;set;}public int ClickEvents {get;set;}public int SelectionEvents {get;set;}
    }
    private readonly Dictionary<string,Row> rows=new();
    private readonly List<object> events=[];
    private readonly List<string> errors=[];
    private readonly HashSet<Window> windows=[];
    private readonly Dictionary<Window,DateTime> captures=new();
    private static string Normalize(string value)=>Regex.Replace(value,@"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}|[a-fA-F0-9]{32}","{id}");
    private static string Text(object? value)=>value switch
    {
        null=>"",string text=>text,TextBlock text=>text.Text,AccessText text=>text.Text,
        Panel panel=>string.Join(" ",panel.Children.OfType<object>().Select(Text)),
        ContentControl content=>Text(content.Content),_=>""
    };
    private static string Label(DependencyObject item)
    {
        var name=AutomationProperties.GetName(item);if(!string.IsNullOrWhiteSpace(name))return Normalize(name);
        var text=item switch {HeaderedItemsControl h=>Text(h.Header),ContentControl c=>Text(c.Content),_=>""};
        if(string.IsNullOrWhiteSpace(text)&&item is FrameworkElement element)text=element.Name;
        return Normalize(text);
    }
    private static Window? Owner(DependencyObject item)
    {
        if(Window.GetWindow(item) is {} window)return window;
        if(item is MenuItem menu)
        {
            for(ItemsControl? parent=ItemsControl.ItemsControlFromItemContainer(menu);parent!=null;parent=ItemsControl.ItemsControlFromItemContainer(parent))
                if(parent is ContextMenu context&&context.PlacementTarget is {} target)return Window.GetWindow(target);
        }
        return null;
    }
    private Row Record(DependencyObject item,Window? owner=null)
    {
        owner??=Owner(item);var title=owner==null?"unattached":owner.GetType().Name+": "+Normalize(owner.Title);
        var label=Label(item);var kind=item.GetType().Name;
        if(label.Length==0&&item is Visual)
        {
            var parts=new List<string>();DependencyObject current=item;
            for(var depth=0;depth<8&&VisualTreeHelper.GetParent(current) is {} parent;depth++)
            {var index=0;for(;index<VisualTreeHelper.GetChildrenCount(parent)&&VisualTreeHelper.GetChild(parent,index)!=current;index++){}parts.Add(parent.GetType().Name+"["+index+"]");current=parent;}
            label="unnamed: "+string.Join("/",parts);
        }
        var key=title+" | "+kind+" | "+label;
        if(!rows.TryGetValue(key,out var row))rows[key]=row=new(){Key=key,Window=title,Type=kind,Label=label};
        if(item is UIElement ui){row.EverVisible|=ui.IsVisible;row.EverEnabled|=ui.IsEnabled;row.EverDisabled|=!ui.IsEnabled;}
        return row;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject item)
    {
        if(item is Visual or System.Windows.Media.Media3D.Visual3D)for(var i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)yield return VisualTreeHelper.GetChild(item,i);
        if(item is FrameworkElement element&&element.ContextMenu is {} menu)yield return menu;
        if(item is ItemsControl items)foreach(var child in items.Items.OfType<DependencyObject>())yield return child;
        if(item is FrameworkElement)foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())yield return child;
    }
    private void Capture(Window window)
    {
        try
        {
            var pending=new Stack<DependencyObject>();pending.Push(window);var seen=new HashSet<DependencyObject>();
            while(pending.TryPop(out var item))
            {
                if(!seen.Add(item))continue;
                if(item is ButtonBase or MenuItem or Selector or TextBox or Slider)Record(item,window);
                foreach(var child in Children(item))pending.Push(child);
            }
        }
        catch(Exception e){Error(e);}
    }
    private void Loaded(object sender,RoutedEventArgs _)
    {
        if(sender is not Window window||!windows.Add(window))return;
        Capture(window);
        window.LayoutUpdated+=(_,_)=>{if(!captures.TryGetValue(window,out var last)||DateTime.UtcNow-last>TimeSpan.FromMilliseconds(150)){captures[window]=DateTime.UtcNow;Capture(window);}};
        window.Closed+=(_,_)=>Capture(window);
    }
    private void Click(object sender,RoutedEventArgs args)
    {
        if(sender is not DependencyObject item)return;
        var row=Record(item);row.ClickEvents++;
        events.Add(new{kind="click",control=row.Key,enabled=(item as UIElement)?.IsEnabled,visible=(item as UIElement)?.IsVisible});
        if(Owner(item) is {} window)window.Dispatcher.BeginInvoke(()=>Capture(window),DispatcherPriority.ContextIdle);
    }
    public void Start()
    {
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler(Loaded),true);
        EventManager.RegisterClassHandler(typeof(ButtonBase),ButtonBase.ClickEvent,new RoutedEventHandler(Click),true);
        EventManager.RegisterClassHandler(typeof(MenuItem),MenuItem.ClickEvent,new RoutedEventHandler(Click),true);
        EventManager.RegisterClassHandler(typeof(Selector),Selector.SelectionChangedEvent,new SelectionChangedEventHandler((sender,_)=>{if(sender is DependencyObject item)Record(item).SelectionEvents++;}),true);
    }
    public void Error(Exception error)=>errors.Add(error.GetType().Name+": "+error.Message);
    public void Save()
    {
        var json=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.ChangeExtension(output,".ui-controls.json"),JsonSerializer.Serialize(new{suite,scope="Installed WPF offline-test controls and routed events; event invocation alone is not a functional assertion or proof of native mouse/keyboard input.",controls=rows.Values.OrderBy(r=>r.Key),errors},json));
        File.WriteAllText(Path.ChangeExtension(output,".ui-events.json"),JsonSerializer.Serialize(events,json));
    }
}
