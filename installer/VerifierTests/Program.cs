using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length!=2)throw new ArgumentException("Supply the staged verifier assembly and an owned temporary fixture directory.");
var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var method=assembly.GetType("Infinity.Installation.Program")!.GetMethod("Verify",BindingFlags.Static|BindingFlags.NonPublic)!;
var root=Path.GetFullPath(args[1]);var temp=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar;
if(!root.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||Directory.Exists(root))throw new IOException("Use a fresh owned fixture folder.");Directory.CreateDirectory(root);
var rows=new Dictionary<string,bool>();var index=0;
string Verify(string folder)=>JsonSerializer.Serialize(method.Invoke(null,[folder]));
string Fixture()
{
    var folder=Path.Combine(root,(index++).ToString());Directory.CreateDirectory(folder);
    foreach(var name in new[]{"LocalAI.Desktop.exe","Infinity.Verify.exe"})File.WriteAllText(Path.Combine(folder,name),"SYNTHETIC_NON_EXECUTING_BYTES");
    var files=new[]{"LocalAI.Desktop.exe","Infinity.Verify.exe"}.Select(name=>new{path=name,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}).ToArray();
    File.WriteAllText(Path.Combine(folder,"installation-manifest.json"),JsonSerializer.Serialize(new{SourceRevision=new string('a',40),Files=files}));File.WriteAllText(Path.Combine(folder,"appsettings.json"),"{\"Models\":[{\"Id\":\"fixture\"}],\"PrivateMarker\":\"SYNTHETIC_PRIVATE_NOT_PRINTED\"}");return folder;
}
bool Refused(string folder){try{Verify(folder);return false;}catch(TargetInvocationException){return true;}}
var good=Fixture();var config=File.ReadAllBytes(Path.Combine(good,"appsettings.json"));var result=Verify(good);rows["validFilesAndConfigurationPass"]=result.Contains("\"Passed\":true");rows["privateSettingsValuesNeverPrintedOrChanged"]=!result.Contains("SYNTHETIC_PRIVATE_NOT_PRINTED")&&File.ReadAllBytes(Path.Combine(good,"appsettings.json")).SequenceEqual(config);
var changed=Fixture();File.WriteAllText(Path.Combine(changed,"LocalAI.Desktop.exe"),"tampered");rows["changedApplicationBytesAreRejected"]=Refused(changed);
var missing=Fixture();File.Delete(Path.Combine(missing,"Infinity.Verify.exe"));rows["missingApplicationFileIsRejected"]=Refused(missing);
var revision=Fixture();var manifest=Path.Combine(revision,"installation-manifest.json");File.WriteAllText(manifest,File.ReadAllText(manifest).Replace(new string('a',40),"not-a-revision"));rows["invalidRevisionIsRejected"]=Refused(revision);
foreach(var (key,path) in new[]{("parentTraversalRejected","../outside.txt"),("absolutePathsRejected",Path.Combine(root,"outside.txt")),("privateConfigurationManifestEntryRejected","appsettings.json")})
{
    var folder=Fixture();File.WriteAllText(Path.Combine(folder,"installation-manifest.json"),JsonSerializer.Serialize(new{SourceRevision=new string('a',40),Files=new[]{new{path,sha256=new string('0',64)}}}));rows[key]=Refused(folder);
}
var empty=Fixture();File.WriteAllText(Path.Combine(empty,"appsettings.json"),"{\"Models\":[]}");rows["emptyProfileConfigurationIsRejected"]=Refused(empty);
var duplicate=Fixture();var original=Path.Combine(duplicate,"LocalAI.Desktop.exe");var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));File.WriteAllText(Path.Combine(duplicate,"installation-manifest.json"),JsonSerializer.Serialize(new{SourceRevision=new string('a',40),Files=new[]{new{path="LocalAI.Desktop.exe",sha256=hash},new{path="LOCALAI.DESKTOP.EXE",sha256=hash}}}));rows["caseDuplicateManifestEntriesRejected"]=Refused(duplicate);
var actual=Path.GetDirectoryName(Path.GetFullPath(args[0]))!;rows["actualStagedApplicationPassesReadOnlyVerification"]=Verify(actual).Contains("\"Passed\":true");
File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows));return rows.Count==11&&rows.Values.All(v=>v)?0:1;
