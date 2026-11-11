using System.Security.Cryptography;
using System.Text;

namespace LocalAI.Core;
public sealed record CodeSavePlan(string Folder,string FileName,string NewContent,string? OriginalContent,string? OriginalHash)
{
    public string TargetPath=>Path.Combine(Folder,FileName);
    public bool Overwrites=>OriginalHash!=null;
}
public sealed record CodeSaveReceipt(string Path,string Hash,string? BackupPath);
public static class ReviewedCodeSave
{
    private const int MaxBytes=1024*1024;
    private static readonly UTF8Encoding Utf8=new(false,true);
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static void ValidatePath(string folder,string name)
    {
        if(string.Equals(name,".git",StringComparison.OrdinalIgnoreCase))throw new IOException("Git metadata files are not save destinations.");
        if(string.IsNullOrWhiteSpace(name)||name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.EndsWith('.')||name.EndsWith(' ')||name is "." or "..")throw new IOException("Use a file name only, without folders, device names or alternate streams.");
        var stem=name.Split('.')[0];if(new[]{"CON","PRN","AUX","NUL"}.Contains(stem,StringComparer.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(COM|LPT)[1-9]$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new IOException("Windows device names cannot be used.");
        if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Choose an existing destination folder.");
        for(var p=Path.GetFullPath(folder);p!=null;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0||Path.GetFileName(p).Equals(".git",StringComparison.OrdinalIgnoreCase))throw new IOException("Reparse points and Git metadata are not save destinations.");
        var target=Path.Combine(folder,name);if(Directory.Exists(target))throw new IOException("Destination is a directory.");
        if(File.Exists(target)&&(File.GetAttributes(target)&(FileAttributes.ReparsePoint|FileAttributes.ReadOnly))!=0)throw new IOException("Destination is linked or read only.");
    }
    private static byte[] ReadBounded(Stream stream)
    {if(stream.Length>MaxBytes)throw new IOException("Reviewed saves are limited to 1 MiB.");var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);return bytes;}
    public static CodeSavePlan Preview(string folder,string name,string code)
    {
        folder=Path.GetFullPath(folder);ValidatePath(folder,name);if(Utf8.GetByteCount(code)>MaxBytes||code.Contains('\0'))throw new IOException("Code must be UTF-8 text of at most 1 MiB.");
        var path=Path.Combine(folder,name);byte[]? original=null;
        if(File.Exists(path)){using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);original=ReadBounded(stream);}
        return new(folder,name,code,original==null?null:Utf8.GetString(original),original==null?null:Hash(original));
    }
    public static CodeSaveReceipt Apply(CodeSavePlan plan,bool confirmOverwrite,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ValidatePath(plan.Folder,plan.FileName);
        var bytes=Utf8.GetBytes(plan.NewContent);if(bytes.Length>MaxBytes||plan.NewContent.Contains('\0'))throw new IOException("Code must be UTF-8 text of at most 1 MiB.");
        if(!plan.Overwrites)
        {
            // CreateNew never overwrites a file that appeared after preview.
            var temporary=Path.Combine(plan.Folder,".localai-"+Guid.NewGuid()+".tmp");
            try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){stream.Write(bytes);stream.Flush(true);stream.Position=0;if(Hash(ReadBounded(stream))!=Hash(bytes))throw new IOException("Saved file verification failed.");}File.Move(temporary,plan.TargetPath,false);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return new(plan.TargetPath,Hash(bytes),null);
        }
        if(!confirmOverwrite)throw new InvalidOperationException("Explicit overwrite confirmation is required.");
        using var locked=new FileStream(plan.TargetPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var original=ReadBounded(locked);if(Hash(original)!=plan.OriginalHash)throw new IOException("File changed since preview. Nothing written; review it again.");
        var backups=Path.Combine(plan.Folder,".localai-backups");Directory.CreateDirectory(backups);if((File.GetAttributes(backups)&FileAttributes.ReparsePoint)!=0)throw new IOException("Backup directory is a reparse point.");
        var backup=Path.Combine(backups,plan.FileName+"."+Guid.NewGuid()+".backup");
        // Preserve the original file's access rules on the recovery copy.
        using(var b=OperatingSystem.IsWindows()
            ?CreatePrivateBackup(backup,locked)
            :new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){b.Write(original);b.Flush(true);}
        try{locked.Position=0;locked.Write(bytes);locked.SetLength(bytes.Length);locked.Flush(true);locked.Position=0;if(Hash(ReadBounded(locked))!=Hash(bytes))throw new IOException("Saved file verification failed.");}
        catch{locked.Position=0;locked.Write(original);locked.SetLength(original.Length);locked.Flush(true);throw;}
        return new(plan.TargetPath,Hash(bytes),backup);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreatePrivateBackup(string path,FileStream original)
    {
        var security=original.GetAccessControl();
        security.SetAccessRuleProtection(true,true);
        return new FileInfo(path).Create(FileMode.CreateNew,System.Security.AccessControl.FileSystemRights.Write,FileShare.None,4096,FileOptions.None,security);
    }
}
