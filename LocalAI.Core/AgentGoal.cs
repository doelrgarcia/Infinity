using System.Security.Cryptography;
using System.Text;

namespace LocalAI.Core;
public sealed class AgentGoal
{
    public string Objective {get;set;}="";
    public string SuccessCriteria {get;set;}="";
    public string CompletionMode {get;set;}="Reviewed build and tests";
    public string State {get;set;}="Paused";
    public int TurnBudget {get;set;}=20;
    public int TurnsUsed {get;set;}
    public int IdleTurns {get;set;}
    public string ProgressHash {get;set;}="";
    public DateTimeOffset? Updated {get;set;}
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Objective)||Objective.Length>16000||string.IsNullOrWhiteSpace(SuccessCriteria)||SuccessCriteria.Length>16000||TurnBudget is <1 or >100||TurnsUsed<0||IdleTurns<0||CompletionMode is not ("Reviewed build and tests" or "User acceptance")||State is not ("Paused" or "Active" or "Waiting for review" or "Needs attention" or "Awaiting acceptance" or "Complete"))throw new IOException("Invalid saved goal; original state retained.");
    }
    public static string Fingerprint(AgentTask task)
    {
        var receipts=task.Messages.Where(m=>m.Role=="user"&&(m.Content.StartsWith("Untrusted tool result for ",StringComparison.Ordinal)||m.Content.StartsWith("Reviewed action receipt (data):",StringComparison.Ordinal))).Select(m=>m.Content).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',receipts)+string.Join('|',task.Evidence.Select(e=>e.Id)))));
    }
    public string FinishTurn(AgentTask task,bool pendingReview,bool verified)
    {
        Updated=DateTimeOffset.UtcNow;
        if(State!="Active")return State;
        if(pendingReview)return State="Waiting for review";
        if(verified)return State="Complete";
        var hash=Fingerprint(task);IdleTurns=hash==ProgressHash?IdleTurns+1:0;ProgressHash=hash;
        if(IdleTurns>=2)return State="Needs attention";
        if(TurnsUsed>=TurnBudget)return State="Paused";
        return State;
    }
}
public sealed class AgentStepLimitException():IOException("Agent reached its 10-step limit. Results and pending reviews are retained. Ask it to continue.");
