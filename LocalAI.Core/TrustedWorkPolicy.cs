using System.Text.RegularExpressions;

namespace LocalAI.Core;

// Safety policy for the Agent's opt-in trusted-work mode. The app still owns
// path checks, backups and change tracking; this only decides what may bypass
// the per-action review queue.
public static class TrustedWorkPolicy
{
    public const int MaximumAutomaticDeletes=3;
    private static readonly Regex DestructiveCommand=new(@"(?i)\b(remove-item|rmdir|rd|rm|del|erase|clear-content|clear-recyclebin|format-volume|initialize-disk|diskpart|git\s+clean|robocopy|xcopy)\b|\[(?:system\.)?io\.(?:file|directory)\]::delete",RegexOptions.Compiled);

    public static bool IsDestructiveCommand(string script)=>DestructiveCommand.IsMatch(script);

    // A folder delete, a batch with more than one delete, or a delete after
    // the automatic-delete budget is exhausted must go through review.
    public static bool RequiresDeleteReview(string operation,int deletesInBatch,int automaticDeletesAlready)
    {
        if(operation=="rmdir")return true;
        if(operation!="delete")return false;
        return deletesInBatch>1||automaticDeletesAlready>=MaximumAutomaticDeletes;
    }
}
