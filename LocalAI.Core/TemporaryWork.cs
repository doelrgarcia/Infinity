namespace LocalAI.Core;

internal static class TemporaryWork
{
    internal static string Create(string name,string fallback,out bool usedFallback)
    {
        var path=Path.Combine(Path.GetTempPath(),name);usedFallback=false;
        try{Probe(path);return path;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){path=Path.Combine(fallback,name);Probe(path);usedFallback=true;return path;}
    }
    private static void Probe(string path){Directory.CreateDirectory(path);var probe=Path.Combine(path,".probe");File.WriteAllText(probe,"");File.Delete(probe);}
    internal static void Remove(string owned){var remaining=10000;Remove(owned,Path.TrimEndingDirectorySeparator(Path.GetFullPath(owned)),0,ref remaining);}
    private static void Remove(string path,string owned,int depth,ref int remaining)
    {
        var full=Path.GetFullPath(path);
        if(full!=owned&&!full.StartsWith(owned+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Temporary cleanup escaped its owned directory.");
        if(depth>64||--remaining<0)throw new IOException("Temporary cleanup reached its safe work limit.");
        var attributes=File.GetAttributes(full);
        if((attributes&FileAttributes.Directory)!=0)
        {if((attributes&FileAttributes.ReparsePoint)==0)foreach(var child in Directory.EnumerateFileSystemEntries(full))Remove(child,owned,depth+1,ref remaining);Directory.Delete(full,false);}
        else File.Delete(full);
    }
}
