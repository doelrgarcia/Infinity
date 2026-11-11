using System.Diagnostics;

namespace LocalAI.Core;
public sealed class RuntimeManager : IDisposable
{
    private Process? owned;
    private string? current;
    private readonly object logLock=new();
    private StreamWriter? log;
    private int logCharacters;
    private readonly string hybridHost;
    private string? temporary;
    public RuntimeManager(string? hybridHostPath=null){hybridHost=hybridHostPath??HybridBackend.DefaultHost;}
    public string? LastLogPath {get;private set;}
    public double? LastLoadSeconds {get;private set;}
    public long? PeakWorkingSet => owned is {HasExited:false} ? owned.PeakWorkingSet64 : null;
    public int? OwnedProcessId => owned is {HasExited:false} ? owned.Id : null;
    public async Task Ensure(ModelProfile model, LocalClient client, CancellationToken token)
    {
        if(current==model.Id && await client.Healthy(model,token)) {LastLoadSeconds=null;return;}
        if(await client.Healthy(model,token)) { Stop(); LastLoadSeconds=null; return; } // Never terminate a runtime started by someone else.
        Stop();
        if(!File.Exists(model.Executable) || (!File.Exists(model.ModelPath)&&!Directory.Exists(model.ModelPath))) throw new FileNotFoundException("Runtime or model missing. Follow LocalAI/README.md installation steps and check appsettings.json.");
        var uri=LocalClient.ValidateEndpoint(model.Endpoint);
        var executable=model.Executable;string? nativeDirectory=null;
        if(HybridBackend.Required(model)){if(!File.Exists(hybridHost))throw new IOException("Infinity's hybrid streaming helper is missing. Republish the complete app; existing models and runtimes were preserved.");nativeDirectory=HybridBackend.VerifyDirectory(model);executable=hybridHost;}
        var info=new ProcessStartInfo(executable) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(model.Executable)!};
        if(nativeDirectory!=null)info.Environment["INFINITY_OGA_DIRECTORY"]=nativeDirectory;
        foreach(var arg in new[]{"-m",model.ModelPath,"--host","127.0.0.1","--port",uri.Port.ToString(),"-c",model.ContextLimit.ToString()}) info.ArgumentList.Add(arg);
        if(model.Engine=="llama") foreach(var arg in new[]{"-ngl","99","--parallel","1","--alias",model.Id}) info.ArgumentList.Add(arg);
        var watch=Stopwatch.StartNew();
        var logs=Path.Combine(AppPaths.DataRoot,"logs");Directory.CreateDirectory(logs);LastLogPath=Path.Combine(logs,"runtime-"+Guid.NewGuid()+".log");
        temporary=TemporaryWork.Create("Infinity-runtime-"+Guid.NewGuid().ToString("N"),Path.Combine(AppPaths.DataRoot,"runtime-temp"),out _);
        info.Environment["TEMP"]=temporary;info.Environment["TMP"]=temporary;
        log=new StreamWriter(new FileStream(LastLogPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){AutoFlush=true};logCharacters=0;
        owned=new Process{StartInfo=info};owned.OutputDataReceived+=WriteLog;owned.ErrorDataReceived+=WriteLog;current=model.Id;
        try
        {
            if(!owned.Start())throw new IOException("Cannot launch local inference runtime.");owned.BeginOutputReadLine();owned.BeginErrorReadLine();
            while(!await client.Healthy(model,token))
            {
                if(owned.HasExited) throw new IOException($"Inference runtime exited with code {owned.ExitCode}. Check driver/model compatibility. Log: {LastLogPath}");
                await Task.Delay(250,token);
            }
            LastLoadSeconds=watch.Elapsed.TotalSeconds;
        }
        catch {Stop();throw;}
    }
    private void WriteLog(object sender,DataReceivedEventArgs e){if(e.Data==null)return;lock(logLock){if(logCharacters<10_000_000 && log!=null){log.WriteLine(e.Data);logCharacters+=e.Data.Length;}}}
    public void Stop() {if(owned!=null){try{if(!owned.HasExited){owned.Kill(true);owned.WaitForExit(5000);}}catch(InvalidOperationException){}owned.Dispose();}owned=null;current=null;lock(logLock){log?.Dispose();log=null;}if(temporary!=null){try{TemporaryWork.Remove(temporary);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){System.Diagnostics.Trace.WriteLine("Runtime temporary cleanup retained "+temporary+": "+e.Message);}temporary=null;}}
    public void Dispose()=>Stop();
}
