using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LocalAI.Core;
public sealed class AgentTaskRequest
{
    public Guid RequestId {get;set;}=Guid.NewGuid();
    public string Title {get;set;}="";
    public string Instructions {get;set;}="";
    public string ProjectRoot {get;set;}="";
    public string ModelId {get;set;}="qwen25-coder";
    public void Validate()
    {
        if(RequestId==Guid.Empty||string.IsNullOrWhiteSpace(Title)||Title.Length>120||string.IsNullOrWhiteSpace(Instructions)||Instructions.Length>16000||string.IsNullOrWhiteSpace(ModelId)||!Path.IsPathFullyQualified(ProjectRoot)||!Directory.Exists(ProjectRoot))throw new IOException("Task request needs an ID, title, instructions, local model ID and an existing full project folder.");
        new FileWorkspace([ProjectRoot]).Resolve(ProjectRoot);
    }
    [JsonIgnore] public string Digest=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
    public static AgentTaskRequest Read(string path)
    {
        var file=new FileInfo(path);if(file.Length>100000)throw new IOException("Task request is too large.");var text=File.ReadAllText(path);using(var doc=JsonDocument.Parse(text)){if(doc.RootElement.ValueKind!=JsonValueKind.Object||doc.RootElement.EnumerateObject().Select(p=>p.Name).Distinct().Count()!=doc.RootElement.EnumerateObject().Count())throw new IOException("Use one task object without duplicate fields.");}var request=JsonSerializer.Deserialize<AgentTaskRequest>(text,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new IOException("Task request is empty.");request.Validate();return request;
    }
}
