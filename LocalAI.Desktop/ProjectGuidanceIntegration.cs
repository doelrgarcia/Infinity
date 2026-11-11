using System.IO;
using LocalAI.Core;

namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private readonly Dictionary<Guid,HashSet<string>> chatGuidanceDirectories=[];
    private IEnumerable<string> ChatInstructionDirectories=>files.Select(f=>Path.GetDirectoryName(f.Path)!).Concat(chatGuidanceDirectories.TryGetValue(conversation.Id,out var saved)?saved:[]);
    private bool LoadChatProposalInstructions(IEnumerable<string> directories)
    {
        var targets=directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var workspace=new FileWorkspace(conversation.WorkspaceRoots);foreach(var path in targets)workspace.Resolve(path);
        var before=ProjectGuidance.Load(conversation.WorkspaceRoots,ChatInstructionDirectories);
        var after=ProjectGuidance.Load(conversation.WorkspaceRoots,ChatInstructionDirectories.Concat(targets));
        if(!chatGuidanceDirectories.TryGetValue(conversation.Id,out var saved))chatGuidanceDirectories[conversation.Id]=saved=new(StringComparer.OrdinalIgnoreCase);
        foreach(var directory in targets)saved.Add(directory);
        if(!after.Except(before).Any())return false;
        status.Text="Additional folder instructions loaded. Ask Infinity to revise this proposal before reviewing it; nothing was applied.";return true;
    }
    private readonly HashSet<string> agentGuidanceDirectories=new(StringComparer.OrdinalIgnoreCase);
    private string? ObserveInstructionScope(AgentCall call,IReadOnlyList<string> roots)
    {
        if(call.Action=="proposeFiles")
        {
            foreach(var action in FileActionProtocol.Parse(call.Value))
            {
                if(ObserveInstructionScope(new("proposeFile",Target:action.Path),roots) is { } notice)return notice;
                if(action.Destination!=null&&ObserveInstructionScope(new("proposeFile",Target:action.Destination),roots) is { } destinationNotice)return destinationNotice;
            }
            return null;
        }
        var candidate=call.Action switch
        {
            "readFile" or "listFiles"=>call.Value,
            "proposeFile"=>call.Target,
            "proposeFolder"=>call.Value,
            "proposeCommand"=>call.Target,
            _=>null
        };
        if(string.IsNullOrWhiteSpace(candidate))return null;
        var resolved=new FileWorkspace(roots).Resolve(candidate);
        var directory=call.Action is "listFiles" or "proposeCommand"?resolved:Path.GetDirectoryName(resolved)!;
        if(agentGuidanceDirectories.Contains(directory))return null;
        var previous=ProjectGuidance.Load(roots,agentGuidanceDirectories);
        var next=ProjectGuidance.Load(roots,agentGuidanceDirectories.Append(directory));agentGuidanceDirectories.Add(directory);
        if(call.Action.StartsWith("propose",StringComparison.Ordinal)&&next.Except(previous).Any())
            return "Additional directory instructions loaded. No proposal was queued. Read the system instruction files for this scope and submit a proposal that follows them.";
        return null;
    }
}
