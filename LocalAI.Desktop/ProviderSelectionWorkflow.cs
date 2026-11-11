using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private void OpenAiProviderSettings()
    {
        if(busy)return;
        new AiToolsWindow(settings,useProvider:profile=>
        {
            model.SelectedItem=profile;
            status.Text=profile.Label+" selected. Your draft and project folder are retained. Each cloud request is reviewed before sending.";
        }){Owner=this}.ShowDialog();
    }
}
