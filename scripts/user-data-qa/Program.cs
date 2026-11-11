using System.Diagnostics;
using System.Text.Json;
using LocalAI.Core;

if(args.Length==1&&args[0]=="--probe")
{
    try
    {
        var settings=Settings.Load(Path.Combine(AppContext.BaseDirectory,"appsettings.json"));
        var privateStoreProtected=false;
        try{new FileWorkspace([AppPaths.DataRoot]).Resolve(Path.Combine(AppPaths.ConversationRoot,"private.json"));}
        catch(IOException){privateStoreProtected=true;}
        if(!privateStoreProtected)throw new IOException("Configured private store was accessible to workspace tools.");
        var store=new ConversationStore(AppPaths.ConversationRoot);var chats=store.Load(out var warning);
        if(warning!="")throw new IOException(warning);
        Console.WriteLine(JsonSerializer.Serialize(new{root=AppPaths.DataRoot,chats=chats.Select(c=>new{c.Id,c.Title,content=c.Messages.Last().Content}),configured=settings.UserDataRoot}));
        return 0;
    }
    catch{Console.Error.WriteLine("Configured data store unavailable.");return 1;}
}
if(args.Length is not (1 or 2))throw new ArgumentException("Supply the owned temporary output folder and optional result path.");
var root=Path.GetFullPath(args[0]);
var temporary=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
if(!root.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(root))throw new IOException("Use a fresh folder inside this process's temporary work root.");
Directory.CreateDirectory(root);var host=Path.Combine(root,"host");Directory.CreateDirectory(host);
foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(host,Path.GetFileName(file)));
var data=Path.Combine(root,"existing-store");Directory.CreateDirectory(data);
var storeFixture=new ConversationStore(Path.Combine(data,"conversations"));var chat=new Conversation{Title="Preserved chat",Messages=[new("user","Existing content")]};storeFixture.Save(chat);
var original=File.ReadAllBytes(Path.Combine(data,"conversations",chat.Id+".json"));
var settingsFixture=new Settings{UserDataRoot=data,Models=[new(){Id="fixture",Engine="llama",Endpoint="http://127.0.0.1:8028"}]};
var config=Path.Combine(host,"appsettings.json");
void Save(string value)=>File.WriteAllText(config,value);
(int Code,string Output) Probe()
{
    using var process=new Process{StartInfo=new("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=host}};
    process.StartInfo.ArgumentList.Add(Path.Combine(host,"UserDataQa.dll"));process.StartInfo.ArgumentList.Add("--probe");
    process.Start();var stdout=process.StandardOutput.ReadToEnd();var stderr=process.StandardError.ReadToEnd();if(!process.WaitForExit(30000)){process.Kill(true);throw new TimeoutException("Data-root probe did not finish.");}return(process.ExitCode,stdout+stderr);
}
var result=new Dictionary<string,bool>();
Save(JsonSerializer.Serialize(settingsFixture));
var first=Probe();var second=Probe();
result["existingStoreAndChatSurviveTwoFreshProcesses"]=first.Code==0&&second.Code==0&&first.Output==second.Output&&first.Output.Contains(chat.Id.ToString())&&first.Output.Contains("Existing content")&&JsonDocument.Parse(first.Output).RootElement.GetProperty("root").GetString()==data;
result["existingChatBytesAreUnchanged"]=File.ReadAllBytes(Path.Combine(data,"conversations",chat.Id+".json")).SequenceEqual(original);
var loaded=Settings.Load(config);Save(JsonSerializer.Serialize(loaded));result["configuredLocationSurvivesSettingsReloadAndSave"]=Probe()==second;
foreach(var (name,bad) in new[]{("relativeLocationRejected","relative"),("missingLocationRejected",Path.Combine(root,"missing")),("networkLocationRejected",@"\\server\share")}){settingsFixture.UserDataRoot=bad;Save(JsonSerializer.Serialize(settingsFixture));result[name]=Probe().Code!=0;}
settingsFixture.UserDataRoot=data;var valid=JsonSerializer.Serialize(settingsFixture);
Save(valid[..^1]+",\"userdataroot\":null}");result["duplicateLocationCannotSilentlySwitchStores"]=Probe().Code!=0;
var linked=Path.Combine(data,"agent");var target=Path.Combine(root,"external");Directory.CreateDirectory(target);
using(var junction=new Process{StartInfo=new("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}})
{
    // Test-owned directories only; never traverse the junction during cleanup.
    Directory.Delete(linked);junction.StartInfo.ArgumentList.Add("/c");junction.StartInfo.ArgumentList.Add("mklink");junction.StartInfo.ArgumentList.Add("/J");junction.StartInfo.ArgumentList.Add(linked);junction.StartInfo.ArgumentList.Add(target);junction.Start();junction.StandardOutput.ReadToEnd();junction.StandardError.ReadToEnd();junction.WaitForExit();if(junction.ExitCode!=0)throw new IOException("Could not prepare junction rejection test.");
}
Save(valid);result["linkedChildStoreIsRejectedWithoutWritingTarget"]=Probe().Code!=0&&!Directory.EnumerateFileSystemEntries(target).Any();Directory.Delete(linked);
File.WriteAllText(args.Length==2?args[1]:Path.Combine(root,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));return result.Values.All(x=>x)?0:1;
