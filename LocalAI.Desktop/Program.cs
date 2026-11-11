using System.Windows;

namespace LocalAI.Desktop;
internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        var app=new Application();
        var qa=args.Length==2&&args[0].EndsWith("-qa",StringComparison.Ordinal);
        void Fail(Exception error)
        {
            if(qa)
            {
                Console.Error.WriteLine("QA failed: "+error.GetType().FullName);
                Console.Error.WriteLine(error.StackTrace); // Types/locations only; no private payloads.
                try{var output=System.IO.Path.GetFullPath(args[1]);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);System.IO.File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(new{error=error.ToString()}));}
                catch(Exception writeError) when(writeError is System.IO.IOException or UnauthorizedAccessException){Console.Error.WriteLine("QA result could not be written: "+writeError.GetType().Name);}
                finally{Environment.ExitCode=1;app.Shutdown(1);}
            }
            else DarkTheme.Error(error.Message);
        }
        app.DispatcherUnhandledException+=(_,e)=>{e.Handled=true;Fail(e.Exception);};
        try
        {
            if(args.Length==2&&args[0]=="--copilot-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await CopilotTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--copilot-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;var window=new MainWindow([]);window.Loaded+=async(_,_)=>await window.ExerciseCopilotLive(args[1]);app.Run(window);return;}
            if(args.Length==2&&args[0]=="--drop-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AttachmentDropTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--ui-agent-paths-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await UiAgentPathTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--ui-auxiliary-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await UiAuxiliaryPathTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--ui-files-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await UiFilePathTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--ui-dialogs-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await UiDialogPathTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--ui-paths-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await UiPathTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--picker-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=(_,_)=>PickerUiTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--hybrid-chat-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await HybridChatTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0] is "--chat-commands-qa" or "--chat-commands-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await ChatCommandTests.Run(args[1],args[0]=="--chat-commands-live-qa");app.Run();return;}
            if(args.Length==2&&args[0]=="--chat-files-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await ChatFileToolTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--chat-files-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await ChatFileToolTests.RunLive(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--agent-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AgentUiTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--task-request"){var window=new MainWindow([]);window.Loaded+=async(_,_)=>await window.StartSubmittedTask(args[1]);app.Run(window);return;}
            if(args.Length==2&&args[0]=="--agent-features-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AgentFeatureTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--agent-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AgentTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--agent-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AgentTests.Run(args[1],true);app.Run();return;}
            if(args.Length==2&&args[0]=="--chat-assistant-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await ChatAssistantTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--debug-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await DebugTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--browser-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await BrowserTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--browser-live-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await BrowserTests.Run(args[1],true);app.Run();return;}
            if(args.Length==2&&args[0]=="--app-controls-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AppControlTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--voice-hardware-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await VoicePromptTests.Run(args[1],true);app.Run();return;}
            if(args.Length==2&&args[0]=="--voice-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await VoicePromptTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--commands-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await CommandTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--source-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await SourceControlTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--files-ui-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await FileWorkspaceTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--desktop-launch-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AiToolsTests.RunDesktop(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--native-setup-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await DependencySetupTests.RunNative(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--tools-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await AiToolsTests.Run(args[1]);app.Run();return;}
            if(args.Length==2&&args[0]=="--setup-qa"){app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Startup+=async(_,_)=>await DependencySetupTests.Run(args[1]);app.Run();return;}
            if(args.Length>0){app.Run(new MainWindow(args));return;}
            app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            app.Startup+=(_,_)=>
            {
                try
                {
                    var settings=LocalAI.Core.Settings.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"appsettings.json"));using var dependencies=new LocalAI.Core.StartupDependencies(settings);
                    var setup=new DependencySetupWindow(settings,dependencies);if(setup.ShowDialog()!=true){app.Shutdown();return;}
                    var ready=dependencies.Inspect().ReadyProfiles;var initial=settings.Models.FirstOrDefault(p=>ready.Contains(p.Id))?.Id;
                    var window=new MainWindow(args,settings,initial);app.MainWindow=window;window.Closed+=(_,_)=>app.Shutdown();window.Show();
                }
                catch(Exception e){DarkTheme.Error(e.Message);app.Shutdown(1);}
            };
            app.Run();
        }
        catch(Exception e) {Fail(e);}
    }
}
