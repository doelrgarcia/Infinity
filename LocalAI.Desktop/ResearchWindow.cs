using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed class ResearchWindow : Window
{
    private readonly WebResearch research=new();
    private readonly CancellationTokenSource closed=new();
    private readonly TextBox query=new();
    private readonly TextBox url=new();
    private readonly ListBox results=new(){SelectionMode=SelectionMode.Extended};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button search=new(){Content="Search web",Margin=new Thickness(8,0,0,0)};
    private readonly Button read=new(){Content="Include public URL",Margin=new Thickness(8,0,0,0)};
    private readonly Button include=new(){Content="Include selected source excerpts",Margin=new Thickness(0,10,0,0)};
    private Task searchTask=Task.CompletedTask;
    public List<WebSource> Sources {get;private set;}=[];
    public ResearchWindow()
    {
        Title="Optional web research";Width=900;Height=630;MinWidth=650;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var root=new DockPanel{Margin=new Thickness(18)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Only this query is sent to DuckDuckGo. Source files and conversation text are not included. Retrieved excerpts stay in this chat until you choose a cloud provider.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        var searchRow=new DockPanel();DockPanel.SetDock(search,Dock.Right);searchRow.Children.Add(search);searchRow.Children.Add(query);top.Children.Add(searchRow);
        search.Click+=(_,_)=>searchTask=Search();
        var openSearch=new Button{Content=UiIcons.Label("Open this search in browser","web"),Margin=new Thickness(0,8,0,0)};System.Windows.Automation.AutomationProperties.SetName(openSearch,"Open this search in browser");top.Children.Add(openSearch);openSearch.Click+=(_,_)=>{try{if(string.IsNullOrWhiteSpace(query.Text)||query.Text.Length>500)throw new InvalidOperationException("Enter a query of 1–500 characters first.");Process.Start(new ProcessStartInfo("https://duckduckgo.com/?q="+Uri.EscapeDataString(query.Text)){UseShellExecute=true});}catch(Exception e){status.Text=e.Message;}};
        var directRow=new DockPanel{Margin=new Thickness(0,10,0,10)};DockPanel.SetDock(read,Dock.Right);directRow.Children.Add(read);directRow.Children.Add(url);top.Children.Add(directRow);url.ToolTip="https:// public source URL";
        read.Click+=async(_,_)=>{read.IsEnabled=false;try{Sources=[await research.ReadSource(url.Text,closed.Token)];DialogResult=true;}catch(Exception e){status.Text=e.Message;}finally{read.IsEnabled=true;}};
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(status);
        bottom.Children.Add(include);
        include.Click+=async(_,_)=>{include.IsEnabled=false;var loaded=new List<WebSource>();var errors=new List<string>();try{var selected=results.SelectedItems.Cast<WebSource>().Take(3).ToArray();if(selected.Length==0){status.Text="Select one or more search results first.";return;}foreach(var source in selected){try{loaded.Add(await research.ReadSource(source.Url,closed.Token));}catch(Exception e){errors.Add(source.Title+": "+e.Message);}}if(loaded.Count>0){Sources=loaded;DialogResult=true;}else status.Text=string.Join("\n",errors);}finally{include.IsEnabled=true;}};
        var open=new Button{Content="Open selected source in browser",Margin=new Thickness(0,8,0,0)};bottom.Children.Add(open);open.Click+=(_,_)=>{if(results.SelectedItem is not WebSource s){status.Text="Select a search result first.";return;}try{Process.Start(new ProcessStartInfo(WebResearch.ValidateUrl(s.Url).AbsoluteUri){UseShellExecute=true});}catch(Exception e){status.Text=e.Message;}};
        System.Windows.Automation.AutomationProperties.SetName(query,"Web search query");System.Windows.Automation.AutomationProperties.SetName(url,"Public source URL");System.Windows.Automation.AutomationProperties.SetName(results,"Web search results");
        status.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)){Source=status});
        root.Children.Add(results);DarkTheme.Wrap(this,root);Closed+=(_,_)=>{closed.Cancel();research.Dispose();closed.Dispose();};
    }
    internal void TestPopulate(){query.Text="ASP.NET Core antiforgery";url.Text="https://learn.microsoft.com/aspnet/core/security/anti-request-forgery";results.ItemsSource=new[]{new WebSource("Synthetic source preview","https://learn.microsoft.com/aspnet/core/","Review a fetched source before including it.",DateTimeOffset.UtcNow)};}
    private async Task Search(){search.IsEnabled=false;status.Text="Searching public web…";try{results.ItemsSource=await research.Search(query.Text,closed.Token);status.Text="Select up to three sources, then Include selected excerpts. Sources can contain errors; review their URLs.";}catch(Exception e){status.Text=e is OperationCanceledException?"Research cancelled.":e.Message;}finally{search.IsEnabled=true;}}
    internal async Task<bool> TestSearch(string value){query.Text=value;search.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await searchTask;return results.Items.Count>0;}
    internal void TestIncludeUrl(string value){url.Text=value;read.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    internal void TestIncludeSelected(string value){results.ItemsSource=new[]{new WebSource("Official QA source",value,"",DateTimeOffset.UtcNow)};results.SelectedIndex=0;include.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
}
