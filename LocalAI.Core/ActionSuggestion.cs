using System.Text.RegularExpressions;

namespace LocalAI.Core;
public sealed class ActionSuggestion
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid? AgentTaskId {get;set;}
    public bool AgentConversation {get;set;}
    public string ModelId {get;set;}="";
    public string Task {get;set;}="";
    public string DoneWhen {get;set;}="";
    public string Scope {get;set;}="";
    public string State {get;set;}="Offered";
    public AppControlRequest? AppAction {get;set;}
    public string LocalWorkflow {get;set;}="";
    public void Validate()
    {
        if(Id==Guid.Empty||ModelId==null||ModelId.Length>200||AgentTaskId.HasValue&&AgentConversation||string.IsNullOrWhiteSpace(Task)||Task.Length>15500||string.IsNullOrWhiteSpace(DoneWhen)||DoneWhen.Length>1500||Scope==null||Scope.Length!=64||State is not ("Offered" or "Accepted" or "Dismissed"))throw new IOException("Invalid saved action suggestion.");
        if(LocalWorkflow is not ("" or "Delete chat" or "Delete project" or "New project")||AppAction!=null&&LocalWorkflow.Length>0)throw new IOException("Unsupported suggested workflow.");
        if(AppAction!=null){if(AppAction.Action==null||AppAction.Value==null)throw new IOException("Invalid saved app action.");AppControlProtocol.Validate(AppAction);}
    }
}
public static class ActionQuestions
{
    public const string Instructions="\nWhen the current user asks a question about carrying out an action, answer the question and offer to help perform the supported action: 'Would you like me to do that?' Do not treat a question as permission to execute it. The app supplies Yes/Not now controls. Be specific about the next supported step and ask for missing details. Do not offer capabilities you lack, promise completion, or bypass file, command, browser or cloud reviews. Pure explanations and explicit requests for information only do not need an action offer. A direct instruction is different from a question; follow its existing reviewed workflow.\n";
    public static bool IsActionQuestion(string text)
    {
        var first=text.Split(['\r','\n'],2)[0].Trim();if(first.Length>6000)return false;
        if(Regex.IsMatch(text,@"\b(?:just explain|only explain|information only|do not (?:do|perform|execute|offer)|don't (?:do|perform|execute|offer)|no actions)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return false;
        return Regex.IsMatch(first,@"^(?:please\s+)?(?:can|could|would|will)\s+(?:you|u|we|I|this|it|that|Infinity)\b|^(?:how\s+(?:do|can|could|would|should)\s+(?:I|we|you)|is it possible to|is there a way to|what(?:'s| is) (?:the )?(?:best )?way to)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))&&(KnownAppQuestion(text)||Regex.IsMatch(first,@"\b(?:add|create|build|fix|update|change|remove|delete|rename|open|start|switch|select|use|write|edit|save|run|test|debug|install|uninstall|set|connect|send|move|copy|organize|export|import|enable|disable|implement|make|deploy|publish|restore|optimize|optimise|simplify|improve|repair|refactor|generate|design|convert|migrate|configure|automate|schedule|stop|pause|resume|pin|unpin|download|upload|organise|merge|commit|push|pull|adding|creating|making|fixing|updating|changing|removing|deleting|renaming|opening|building|implementing)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)));
    }
    private static bool KnownAppQuestion(string text){try{return AppControlProtocol.FromPrompt(text)!=null;}catch(IOException){return false;}}
    public static ActionSuggestion? Offer(string question,string answer,string scope)
    {
        if(question.Length>15000||!IsActionQuestion(question)||string.IsNullOrWhiteSpace(answer)||answer.Contains("[Interrupted:",StringComparison.Ordinal)||answer.Contains("[Output limit reached",StringComparison.Ordinal)||CliEscalation.RequestsHelp(answer)||Regex.IsMatch(answer,@"\b(?:cannot|can't|unable|not able|not supported|don't have access)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return null;
        return new(){Task="Help carry out: "+question,DoneWhen="Show me what changed and how it was checked, then let me confirm the result. Ask for missing details and explain unsupported steps rather than pretending they were completed.",Scope=scope};
    }
}
