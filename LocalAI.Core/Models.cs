using System.Text.Json;

namespace LocalAI.Core;
public sealed record Message(string Role, string Content, string? Provider=null,IReadOnlyList<ImageInput>? Images=null,ActionSuggestion? Suggestion=null,IReadOnlyList<FileAttachment>? Files=null);
public sealed class Conversation
{
    public DateTimeOffset? DeletedAt {get;set;}
    public List<LiveFollowUp> FollowUps {get;set;}=[];
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New conversation";
    public string ModelId { get; set; } = "";
    public Guid? ProjectId {get;set;}
    public string TaskBrief { get; set; } = "";
    public string ActivityState {get;set;}="Not tracked";
    public string ActivityDetail {get;set;}="";
    public DateTimeOffset? LastActivityUtc {get;set;}
    public bool NeedsAttention {get;set;}
    public string ResponseStyle { get; set; } = "Build";
    public bool AutoCliFallback { get; set; } = true;
    public string FallbackProviderId { get; set; } = "cloud-codex";
    public List<string> WorkspaceRoots { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
    public override string ToString() => Title;
}
public sealed class ModelProfile
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Executable { get; set; } = "";
    public bool TrustBridgeWorkspace {get;set;}
    public string ModelPath { get; set; } = "";
    public string Engine { get; set; } = "llama";
    public int ContextLimit { get; set; } = 8192;
    public bool IsCloud=>Engine is "codex" or "cursor" or "grok" or "deepseek" or "copilot";
    public override string ToString() => Label;
}
public sealed class Settings
{
    public int LoadTimeoutSeconds { get; set; } = 600;
    public int GenerationTimeoutSeconds { get; set; } = 600;
    public string? UserDataRoot {get;set;}
    public List<ModelProfile> Models { get; set; } = [];
    public static Settings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings.");
        if(settings.Models.Count==0||settings.Models.Any(m=>string.IsNullOrWhiteSpace(m.Id))||settings.Models.GroupBy(m=>m.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("Configure at least one model with a unique nonempty ID.");
        if(settings.LoadTimeoutSeconds is < 1 or > 3600 || settings.GenerationTimeoutSeconds is < 1 or > 3600)
            throw new InvalidDataException("Timeouts must be between 1 and 3600 seconds.");
        foreach(var model in settings.Models)
        {
            if(!model.IsCloud)LocalClient.ValidateEndpoint(model.Endpoint);
            if(model.ContextLimit is < 512 or > 131072 || model.Engine is not ("llama" or "ryzen" or "codex" or "cursor" or "grok" or "deepseek" or "copilot")) throw new InvalidDataException("Invalid model settings.");
        }
        DeepSeekBridge.AddProvider(settings);CopilotBridge.AddProvider(settings);return settings;
    }
}
public sealed class ConversationStore(string directory)
{
    private sealed class Fingerprint {public string Hash="";}
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Conversation,Fingerprint> fingerprints=new();
    public void EnsureCurrent(Conversation chat)
    {
        var path=Path.Combine(directory,chat.Id+".json");
        if(!fingerprints.TryGetValue(chat,out var known)){if(File.Exists(path))throw new IOException("This chat has changed outside this window. Reopen Infinity before managing it.");return;}
        if(!File.Exists(path)||Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))!=known.Hash)throw new IOException("This chat has changed outside this window. Reopen Infinity before managing it; your draft is retained.");
    }
    public List<Conversation> Load(out string warning)
    {
        Directory.CreateDirectory(directory); warning=""; var result=new List<Conversation>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            try
            {
                var bytes=File.ReadAllBytes(file);var c=JsonSerializer.Deserialize<Conversation>(bytes);
                if(c==null||c.Id==Guid.Empty||c.Messages==null||c.Messages.Any(m=>m==null||m.Content==null||m.Role is not ("user" or "assistant"))||result.Any(existing=>existing.Id==c.Id))
                    throw new JsonException("Invalid or duplicate saved conversation.");
                foreach(var m in c.Messages)m.Suggestion?.Validate();FileAttachment.All(c.Messages);ImageInput.All(c.Messages);c.FollowUps??=[];LiveFollowUp.ValidateAll(c.FollowUps);
                c.Title=string.IsNullOrWhiteSpace(c.Title)?"New conversation":c.Title;c.ModelId??="";c.TaskBrief??="";
                c.WorkspaceRoots=(c.WorkspaceRoots??[]).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
                c.ActivityState=c.ActivityState is "Working" or "Response ready" or "Needs attention" or "Stopped"?c.ActivityState:"Not tracked";c.ActivityDetail??="";
                c.ResponseStyle=c.ResponseStyle is "Ask" or "Plan"?c.ResponseStyle:"Build";fingerprints.GetValue(c,_=>new Fingerprint()).Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));result.Add(c);
            }
            catch(Exception e) when(e is JsonException or IOException or UnauthorizedAccessException) { warning="One or more saved conversations could not be read; original files retained."; }
        }
        return result;
    }
    public void Save(Conversation conversation)
    {
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,conversation.Id+".json");
        using var lease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        EnsureCurrent(conversation);
        var temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
        { JsonSerializer.Serialize(stream,conversation); stream.Flush(true); }
        File.Move(temp,path,true);
        fingerprints.GetValue(conversation,_=>new Fingerprint()).Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(conversation)));
    }
}
