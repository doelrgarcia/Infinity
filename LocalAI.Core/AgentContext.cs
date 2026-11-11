using System.Text.Json;
namespace LocalAI.Core;

// Trim only the inference packet. The persisted conversation remains the audit trail.
public static class AgentContext
{
    public static bool WasDeclined(FileAction proposed,IEnumerable<AgentApproval> approvals)
    {
        if(proposed.Operation!="write")return false;
        foreach(var approval in approvals.Where(a=>a.Kind=="files"&&a.Status=="Declined"))
        {
            try{if(FileActionProtocol.Parse(approval.Value).Any(a=>a.Operation=="write"&&a.Content==proposed.Content&&string.Equals(Path.GetFullPath(a.Path),Path.GetFullPath(proposed.Path),StringComparison.OrdinalIgnoreCase)))return true;}
            catch(Exception e) when(e is IOException or JsonException or ArgumentException){/* Malformed historical proposals provide no match. */}
        }
        return false;
    }
    public static IReadOnlyList<Message> ForModel(IReadOnlyList<Message> packet,IEnumerable<AgentApproval> approvals)
    {
        var declined=approvals.Where(a=>a.Status=="Declined").ToArray();
        return packet.Select(message=>
        {
            if(message.Role!="assistant")return message;
            AgentCall call;try{call=AgentProtocol.Parse(message.Content);}catch(Exception e) when(e is IOException or JsonException){return message;}
            if(call.Action=="proposeFile"&&WasDeclined(new("write",call.Target,Content:call.Content),declined))
                return new Message("assistant",JsonSerializer.Serialize(new{action="reply",value="Earlier source proposal was declined and its body is omitted from this model input. It was not applied. Read the user's review/compiler findings and propose corrected source, not the same draft. Destination (data): "+call.Target}));
            if(call.Action=="reply"&&(call.Content.Length>0||call.Target.Length>0))
                return new Message("assistant",JsonSerializer.Serialize(new{action="reply",value="Earlier assistant reply included ignored action fields. It is not controller status or an applied file. Inspect actual tool receipts and the latest user request."}));
            return message;
        }).ToArray();
    }
    public static bool IsRequest(Message message)=>message.Role=="user"&&!new[]{"Untrusted tool result for ","Reviewed action receipt (data):","Protocol error;"}.Any(p=>message.Content.StartsWith(p,StringComparison.Ordinal));

    public static async Task<IReadOnlyList<Message>> Fit(IReadOnlyList<Message> packet,int budget,Func<IReadOnlyList<Message>,CancellationToken,Task<int>> count,CancellationToken token)
    {
        var indexed=packet.Select((message,index)=>(message,index)).ToList();
        var requests=indexed.Where(x=>IsRequest(x.message)&&x.index>1).ToArray();
        var required=new HashSet<int>(indexed.Take(2).Concat(indexed.TakeLast(2)).Select(x=>x.index));
        if(requests.Length>0){required.Add(requests[0].index);required.Add(requests[^1].index);}
        foreach(var item in indexed.Where(x=>x.message.Role=="system"))required.Add(item.index);
        var omitted=0;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var candidate=indexed.Select(x=>x.message).ToList();
            if(omitted>0)candidate.Insert(1,new("system",$"{omitted} older history messages omitted from this inference packet to fit context. Full history is retained. Do not infer missing file contents or successful actions. Read current files and receipts as needed."));
            if(await count(candidate,token)<=budget)return candidate;
            var at=indexed.FindIndex(x=>!required.Contains(x.index));
            if(at<0)throw new IOException("The current request and required scope exceed this model's context. Shorten the request or review a stronger-model handoff; history was retained.");
            indexed.RemoveAt(at);omitted++;
        }
    }
}
