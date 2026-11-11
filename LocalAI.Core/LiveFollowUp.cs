namespace LocalAI.Core;
public sealed class LiveFollowUp
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public string Text {get;set;}="";
    public string Mode {get;set;}="Queue";
    public string Status {get;set;}="Pending";
    public DateTimeOffset Created {get;set;}=DateTimeOffset.UtcNow;
    public IReadOnlyList<ImageInput>? Images {get;set;}
    public IReadOnlyList<FileAttachment>? Files {get;set;}
    public void Validate(){if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Text)||Text.Length>16000||Mode is not ("Queue" or "Steer")||Status is not ("Pending" or "Applied"))throw new IOException("Invalid saved follow-up.");ImageInput.All([new("user",Text,Images:Images)]);FileAttachment.ValidateDraft(Files??[]);}
    public static void ValidateAll(List<LiveFollowUp> items){if(items.Count>100||items.Any(i=>i==null)||items.Select(i=>i.Id).Distinct().Count()!=items.Count)throw new IOException("Follow-up history is full or invalid.");foreach(var item in items)item.Validate();}
}
