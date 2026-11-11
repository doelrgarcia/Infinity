namespace LocalAI.Core;

public sealed record CliHandoffPacket(IReadOnlyList<Message> Messages,int OriginalCharacters,int OmittedCharacters,int OmittedMessages)
{
    public string Prompt=>CliAssistant.FormatPrompt(Messages);
}

public static class CliEscalation
{
    public const string HelpMarker="[INFINITY_NEEDS_CLI]";
    public static bool RequestsHelp(string response)=>response.TrimStart().StartsWith(HelpMarker,StringComparison.Ordinal);

    // Preserve the current request and system rules first. Bounded excerpts are reviewed before transmission.
    public static CliHandoffPacket Prepare(IReadOnlyList<Message> messages,string reason,IReadOnlyList<string>? fileRoots=null)
    {
        if(messages.Count<2||messages[0].Role!="system"||messages[^1].Role!="user")throw new ArgumentException("A handoff needs system rules and a current user request.");
        var fileAware=fileRoots is {Count:>0}||messages[0].Content.Contains(FileActionProtocol.Language,StringComparison.OrdinalIgnoreCase);
        var note=ChatFileTools.Enabled(messages)
            ?"\n\nInfinity CLI handoff: "+reason+". Connected-folder chat file tools are enabled. Return file requests using the documented protocol; Infinity executes screened reads/writes/mkdir locally inside connected folders and returns actual receipts. Other file operations require review. You cannot execute tools or modify files directly. Only returned receipts prove execution. Context may contain marked excerpts; request missing contents before editing."
            :fileAware
            ?"\n\nInfinity CLI handoff: "+reason+". This chat has connected folders. You cannot execute tools or modify files directly. For file tasks, return a fenced "+FileActionProtocol.Language+" JSON array of proposed actions using absolute paths inside the connected folders; Infinity shows every action for user review and only reviewed results prove execution. Never claim a proposed action was applied. Context may contain explicitly marked excerpts; ask for missing details rather than inventing them."
            :"\n\nInfinity CLI handoff: "+reason+". Help with the current request. Context may contain explicitly marked excerpts. Ask for missing details rather than inventing them. Return text/code only; never execute tools or change files.";
        if(messages.Any(m=>m.Role=="system"&&m.Content.Contains(ChatCommands.Instructions,StringComparison.Ordinal)))note+="\nThis CLI is the model transport for Infinity's coding loop. Return Infinity file/command requests as text using the supplied protocols. Infinity owns execution and reviews and will send actual receipts back. Do not call your CLI's native shell/file tools, and do not say the user must run tests manually when Infinity can run a reviewed command.";
        var system=messages[0].Content;
        var needsFileProtocol=fileRoots is {Count:>0}&&!system.Contains(FileActionProtocol.Language,StringComparison.OrdinalIgnoreCase);
        if(fileAware)system=system.Replace("Do not execute commands, use tools or modify files; only respond with text/code.","Do not execute commands or modify files directly. For connected-folder work, propose reviewed infinity-files actions.");
        if(needsFileProtocol)system+=FileActionProtocol.Instructions(fileRoots!);
        var middle=Enumerable.Range(1,messages.Count-2).Where(i=>messages[i].Role!="system").ToArray();
        var selected=middle.Take(2).Concat(middle.TakeLast(16)).Concat(middle.Where(i=>messages[i].Images?.Count>0)).Distinct().Order().ToArray();
        var omittedMessages=middle.Length-selected.Length;
        var omitted=middle.Except(selected).Sum(i=>messages[i].Content.Length);
        var packet=new List<Message>{messages[0] with {Content=system+note}};
        packet.AddRange(messages.Skip(1).Where(m=>m.Role=="system"));
        string Excerpt(string content,int limit)
        {
            if(content.Length<=limit)return content;
            const string marker="\n[... excerpt omitted for CLI context limit ...]\n";
            if(limit<marker.Length)throw new IOException("Required instructions and image metadata exceed the CLI context limit. Use a smaller scope; instructions were not truncated.");
            var retained=limit-marker.Length;var head=(retained+1)/2;omitted+=content.Length-retained;
            return content[..head]+marker+(retained-head==0?"":content[^(retained-head)..]);
        }
        var fixedSize=CliAssistant.FormatPrompt(packet.Concat(selected.Select(i=>messages[i] with {Content=""})).Append(messages[^1] with {Content=""}).ToArray()).Length;
        var available=CliAssistant.MaximumPromptCharacters-fixedSize;
        var request=messages[^1] with {Content=Excerpt(messages[^1].Content,available-selected.Length*80)};
        var remaining=available-request.Content.Length;
        for(var n=0;n<selected.Length;n++)
        {
            var message=messages[selected[n]];var excerpt=Excerpt(message.Content,remaining/(selected.Length-n));remaining-=excerpt.Length;packet.Add(message with {Content=excerpt});
        }
        packet.Add(request);
        if(CliAssistant.FormatPrompt(packet).Length>CliAssistant.MaximumPromptCharacters)throw new InvalidOperationException("Handoff context could not fit the CLI bridge limit.");
        return new(packet,CliAssistant.FormatPrompt(messages).Length,omitted,omittedMessages);
    }
}
