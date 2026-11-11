using System.IO;
using System.Text.Json;
using System.Windows;
using LocalAI.Core;

namespace LocalAI.Desktop;

public sealed partial class MainWindow
{
    private Action<CommandWindow>? chatCommandReviewTest;
    private async Task<string> RunChatCommand(CommandRequest request,CancellationToken token)
    {
        await Task.Yield(); // Paint the working state before opening review.
        token.ThrowIfCancellationRequested();
        if(conversation.ResponseStyle=="Plan")return ChatCommands.ResultPrefix+JsonSerializer.Serialize(new{status="Not executed",reason="Plan mode does not run commands. Switch to Build for execution."});
        var roots=conversation.WorkspaceRoots.ToArray();
        try
        {
            if(LoadChatProposalInstructions([request.WorkingDirectory]))return ChatCommands.ResultPrefix+JsonSerializer.Serialize(new{status="Not executed",reason="Additional folder instructions loaded. Read them before proposing the command again."});
            var runner=new CommandRunner(roots);request=runner.Preview(request).Request;
            var dialog=new CommandWindow(roots,request,logRoot:smokeOutput==null?null:Path.Combine(Path.GetDirectoryName(smokeOutput)!,"command-logs"),closeAfterRun:true,ownerCancellation:token,beforeRun:edited=>
            {
                token.ThrowIfCancellationRequested();
                if(LoadChatProposalInstructions([edited.WorkingDirectory]))throw new IOException("Additional folder instructions were loaded. Close this review so the assistant can read them before proposing execution again.");
            }){Owner=this};
            using var cancellation=token.Register(()=>Dispatcher.BeginInvoke(new Action(()=>dialog.CancelForOwner())));
            if(chatCommandReviewTest!=null)dialog.Loaded+=(_,_)=>chatCommandReviewTest(dialog);
            dialog.ShowDialog();
            return ChatCommands.ResultPrefix+(dialog.Result==null?JsonSerializer.Serialize(new{status="Declined; not executed",request.Script,request.WorkingDirectory}):JsonSerializer.Serialize(dialog.Result));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {return ChatCommands.ResultPrefix+JsonSerializer.Serialize(new{status="Not executed",error=e.Message});}
    }
}
