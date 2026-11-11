using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly ContentControl agentSurface=new(){Visibility=Visibility.Collapsed};
    private readonly Button agentTab=new(){Content="Agent",Padding=new Thickness(12,5,12,5)};
    private DockPanel? workspaceCenter,inspectorHost;
    private bool AgentSurfaceActive=>agentSurface.Visibility==Visibility.Visible;

    private void ShowAgentSurface(UserControl view)
    {
        returnToChat.Visibility=Visibility.Visible;
        CancelDictation();
        if(agentSurface.Content==agentView&&view!=agentView)agentView?.SaveDraftAndStopVoice();
        agentSurface.Content=view;agentSurface.Visibility=Visibility.Visible;
        chatHeading.Visibility=Visibility.Collapsed;
        chatView.Visibility=Visibility.Collapsed;codeView.Visibility=Visibility.Collapsed;
        if(workspaceCenter!=null)Grid.SetColumnSpan(workspaceCenter,3);
        if(inspectorHost!=null)inspectorHost.Visibility=Visibility.Collapsed;
        if(inspectorSplitter!=null)inspectorSplitter.Visibility=Visibility.Collapsed;
        agentTab.Background=Brush("#232D43");chatTab.Background=codeTab.Background=Brush("#101318");
    }
    private void LeaveAgentSurface()
    {
        if(!AgentSurfaceActive)return;
        agentView?.SaveDraftAndStopVoice();agentSurface.Visibility=Visibility.Collapsed;
        chatHeading.Visibility=Visibility.Visible;
        if(workspaceCenter!=null)Grid.SetColumnSpan(workspaceCenter,1);
        var visible=inspectorColumn.Width.Value>0;
        if(inspectorHost!=null)inspectorHost.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(inspectorSplitter!=null)inspectorSplitter.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        agentTab.Background=Brush("#101318");
    }
    private void CloseAgentView()
    {
        if(agentView==null)return;
        if(agentReviewDepth>0)throw new IOException("Finish the current review first.");
        agentView.SaveDraftAndStopVoice();
        var shown=agentSurface.Content==agentView;agentView=null;
        if(shown){agentSurface.Content=null;ShowChat();}
    }
    private void CloseAgentWorkbench()
    {
        if(agentWorkbench==null)return;
        if(agentWorkbench.IsWorking)throw new IOException("Wait for the active workspace action to finish.");
        agentWorkbench.CancelSearch();var shown=agentSurface.Content==agentWorkbench;agentWorkbench=null;
        if(shown)OpenAgent();
    }
}
