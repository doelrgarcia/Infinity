using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private void OpenChatSkills()
    {
        try
        {
            if(ChatManagementBlocked)throw new IOException("Finish the current chat operation before choosing a skill.");
            ShowChat();var dialog=new SkillsWindow(SkillLibrary.Discover(conversation.WorkspaceRoots)){Owner=this};
            if(dialog.ShowDialog()==true&&dialog.SelectedSkill is {} skill){prompt.Text=(prompt.Text.Length==0?"":prompt.Text+"\n")+"$"+skill.Name+" ";prompt.CaretIndex=prompt.Text.Length;prompt.Focus();}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Skills could not open: "+e.Message;}
    }
    private void ReviewChatInstructions()
    {
        try
        {
            var documents=ProjectGuidance.Load(conversation.WorkspaceRoots,ChatInstructionDirectories);
            AgentView.ReviewText(this,"Project instructions",documents.Count==0?"No applicable AGENTS.md files in this chat's connected folders.":string.Join("\n\n",documents.Select(d=>d.Path+"\n"+d.Content)),false);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Project instructions: "+e.Message;}
    }
}
internal sealed class SkillsWindow:Window
{
    internal LocalSkill? SelectedSkill {get;private set;}
    internal SkillsWindow(IReadOnlyList<LocalSkill> skills)
    {
        Title="Project skills";Width=700;Height=550;MinWidth=480;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;DarkTheme.Apply(this);
        var body=new DockPanel{Margin=new Thickness(24)};var help=new TextBlock{Text="Reusable workflows from .agents/skills or .infinity/skills inside connected folders. Choose one to add its $name to your prompt. Nothing runs until you send; file changes and scripts still require review.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(help,Dock.Top);body.Children.Add(help);
        var pick=new ComboBox{ItemsSource=skills,DisplayMemberPath="Name",Margin=new Thickness(0,0,0,12)};System.Windows.Automation.AutomationProperties.SetName(pick,"Project skill");DockPanel.SetDock(pick,Dock.Top);body.Children.Add(pick);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);var use=new Button{Content="Use in prompt",IsEnabled=skills.Count>0};actions.Children.Add(use);
        var details=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};System.Windows.Automation.AutomationProperties.SetName(details,"Skill instructions");body.Children.Add(details);pick.SelectionChanged+=(_,_)=>{if(pick.SelectedItem is LocalSkill skill)details.Text=skill.Description+"\n\n"+skill.Path+"\n\n"+skill.Instructions;};
        if(skills.Count>0)pick.SelectedIndex=0;else details.Text="No project skills found. Connect a folder, then add a workflow at .agents/skills/<name>/SKILL.md with name and description front matter and the workflow instructions.";
        use.Click+=(_,_)=>{if(pick.SelectedItem is LocalSkill skill){SelectedSkill=skill;DialogResult=true;}};DarkTheme.Wrap(this,body);
    }
}
