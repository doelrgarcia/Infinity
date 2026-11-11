using System.Security.Cryptography;

namespace LocalAI.Core;
// Immutable snapshots: later edits to the original screenshot cannot change a reviewed send.
public sealed record ImageInput(string Name,string Mime,string Base64,string Sha256)
{
    public const int MaximumBytes=2*1024*1024;
    public byte[] Bytes()
    {
        if(Name==null||Sha256==null||Base64==null||Base64.Length>MaximumBytes*4/3+8)throw new IOException("Image exceeds 2 MiB.");
        byte[] bytes;try{bytes=Convert.FromBase64String(Base64);}catch(FormatException){throw new IOException("Invalid saved image.");}
        if(bytes.Length is < 12 or > MaximumBytes||Name.Length is <1 or >160||Name.Any(char.IsControl)||Path.GetFileName(Name)!=Name)throw new IOException("Invalid image name or size.");
        var mime=Detect(bytes);if(mime!=Mime||Convert.ToHexString(SHA256.HashData(bytes))!=Sha256)throw new IOException("Image type or fingerprint changed.");return bytes;
    }
    public static string Detect(byte[] bytes)=>bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255?"image/jpeg":throw new IOException("Choose a PNG or JPEG screenshot.");
    public static ImageInput FromBytes(string name,byte[] bytes)
    {
        if(bytes.Length is <12 or >MaximumBytes)throw new IOException("Each screenshot must be at most 2 MiB.");
        var image=new ImageInput(Path.GetFileName(name),Detect(bytes),Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)));image.Bytes();return image;
    }
    public static ImageInput[] All(IEnumerable<Message> messages)
    {
        var entries=messages.SelectMany(m=>m.Images??[]).ToArray();if(entries.Any(i=>i==null))throw new IOException("Invalid saved image attachment.");foreach(var image in entries)image.Bytes();var images=entries.DistinctBy(i=>i.Sha256).ToArray();
        if(images.Length>4)throw new IOException("Use at most four screenshots per request, including history. Start a new chat for more.");
        foreach(var image in images)image.Bytes();return images;
    }
    public static void RequireProvider(ModelProfile profile,IEnumerable<Message> messages)
    {
        if(All(messages).Length>0&&profile.Engine!="codex")throw new IOException("Screenshots currently require the Codex provider. Select Codex and review the exact images before sending; your draft is retained.");
    }
}
