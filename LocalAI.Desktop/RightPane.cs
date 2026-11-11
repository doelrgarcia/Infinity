using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly ContentControl rightPaneBody=new();
    private readonly DockPanel workspaceInspector=new();
    private readonly DockPanel documentPane=new();
    private readonly TextBlock documentTitle=new(){TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,8,0,8)};
    private readonly TextBox documentText=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
    private FrameworkElement? dockedBrowser;
    private string rightPaneMode="Workspace";
    private string? documentPath;
    private readonly Dictionary<Guid,(string Text,string? Path)> documentPreviews=[];
    private void OpenDocument()
    {
        try{if(busy)return;var picker=new SourcePicker(false){Owner=this};if(pickerTestAction!=null)picker.Loaded+=(_,_)=>pickerTestAction(picker);if(picker.ShowDialog()!=true)return;var source=ContextFiles.Read(picker.SelectedPaths.First());OpenCode(source.Content,System.IO.Path.GetExtension(source.Path).TrimStart('.'),source.Path,true);}
        catch(Exception e){status.Text="Document could not open: "+e.Message;}
    }
    private void BuildRightPane(DockPanel host)
    {
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(actions,Dock.Top);host.Children.Add(actions);
        actions.Children.Add(FlatButton("Workspace",()=>ShowRightPane("Workspace")));
        actions.Children.Add(FlatButton("Document",()=>ShowRightPane("Document")));
        actions.Children.Add(FlatButton("Browser",()=>OpenBrowser()));
        actions.Children.Add(FlatButton("Close",HideRightPane,"Close right pane"));host.Children.Add(rightPaneBody);rightPaneBody.Content=workspaceInspector;
        DockPanel.SetDock(documentTitle,Dock.Top);documentPane.Children.Add(documentTitle);
        var edit=new Button{Content="Make editable draft",Margin=new Thickness(0,8,0,8)};DockPanel.SetDock(edit,Dock.Bottom);documentPane.Children.Add(edit);edit.Click+=(_,_)=>{if(busy||documentText.Text.Length==0)return;OpenCode(documentText.Text,System.IO.Path.GetExtension(documentPath??"").TrimStart('.'),documentPath,true);editorDraft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));};
        documentPane.Children.Add(documentText);System.Windows.Automation.AutomationProperties.SetName(documentText,"Document preview");
    }
    private void ShowRightPane(string mode,FrameworkElement? browser=null)
    {
        if(browser!=null)dockedBrowser=browser;
        rightPaneMode=mode;rightPaneBody.Content=mode switch{"Document"=>documentPane,"Browser"=>dockedBrowser,_=>workspaceInspector};
        inspectorColumn.MaxWidth=700;inspectorColumn.MinWidth=250;
        var available=ActualWidth-navigationColumn.Width.Value-440;
        inspectorColumn.Width=new GridLength(Math.Clamp(available,250,mode=="Workspace"?320:620));
        if(inspectorHost!=null)inspectorHost.Visibility=Visibility.Visible;if(inspectorSplitter!=null)inspectorSplitter.Visibility=Visibility.Visible;if(workspaceCenter!=null)Grid.SetColumnSpan(workspaceCenter,1);
    }
    private void HideRightPane()
    {
        inspectorColumn.MinWidth=0;inspectorColumn.Width=new GridLength(0);if(inspectorHost!=null)inspectorHost.Visibility=Visibility.Collapsed;if(inspectorSplitter!=null)inspectorSplitter.Visibility=Visibility.Collapsed;
        if(AgentSurfaceActive&&workspaceCenter!=null)Grid.SetColumnSpan(workspaceCenter,3);
    }
    private void ShowDocument(string text,string? path)
    {
        documentPreviews[conversation.Id]=(text,path);SetDocument(text,path);ShowChat();ShowRightPane("Document");
    }
    private void SetDocument(string text,string? path){documentPath=path;documentTitle.Text=path==null?"Document":System.IO.Path.GetFileName(path);documentTitle.ToolTip=path;documentText.Text=text;}
    private void RestoreDocument(){if(documentPreviews.TryGetValue(conversation.Id,out var doc))SetDocument(doc.Text,doc.Path);else{SetDocument("",null);if(rightPaneMode=="Document")HideRightPane();}}
}
