namespace LocalAI.Core;
public sealed record ChatOverview(Guid Id,string Title,string State,bool NeedsAttention,int MessageCount,DateTimeOffset? Updated,bool IsCurrent,bool CanStop,string Detail)
{
    public string Summary=>$"{Title}\n{State}"+(NeedsAttention?" • Needs attention":"")+$" • {MessageCount} messages";
}
public static class ChatMonitor
{
    public static ChatOverview Describe(Conversation chat,Guid current,bool working,string liveDetail="")
    {
        var active=chat.Id==current&&working;
        var state=active?"Working":chat.ActivityState=="Working"?"Saved running state — not verified live":chat.ActivityState;
        return new(chat.Id,chat.Title,state,chat.NeedsAttention,chat.Messages.Count,chat.LastActivityUtc,chat.Id==current,active,active?liveDetail:chat.ActivityDetail);
    }
    public static void Record(Conversation chat,string state,string detail,bool attention=false)
    {
        if(state is not ("Working" or "Response ready" or "Needs attention" or "Stopped"))throw new ArgumentOutOfRangeException(nameof(state));
        chat.ActivityState=state;chat.ActivityDetail=detail[..Math.Min(detail.Length,400)];chat.LastActivityUtc=DateTimeOffset.UtcNow;if(attention)chat.NeedsAttention=true;
    }
}
