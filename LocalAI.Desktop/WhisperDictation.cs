using System.IO;
using System.Security.Cryptography;
using NAudio.Wave;
using Whisper.net;
using LocalAI.Core;
using System.Windows.Threading;
namespace LocalAI.Desktop;
internal sealed class WhisperDictation : IDictationSession
{
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer limit;
    private readonly CancellationTokenSource cancel=new();
    private readonly MemoryStream audio=new();
    private readonly object gate=new();
    private WaveInEvent? input;
    private readonly int device;
    private bool disposed,stopping;
    private string? model;
    public event Action<string>? Recognized;
    public event Action<string?>? Completed;
    public event Action<string>? Progress;
    public event Action<double>? Level;
    internal bool Listening=>input!=null&&!stopping;
    internal WhisperDictation(Dispatcher dispatcher,int device=-1){this.dispatcher=dispatcher;this.device=device;limit=new DispatcherTimer(TimeSpan.FromMinutes(2),DispatcherPriority.Normal,(_,_)=>Stop(),dispatcher);limit.Stop();}
    public async void Start()
    {
        try
        {
            Progress?.Invoke("Preparing local Whisper voice…");
            model=await WhisperVoiceAssets.Ensure(new Progress<DependencyProgress>(p=>{if(!disposed)Progress?.Invoke($"Setting up local voice • {p.Phase} • {p.Completed/1000000:N0} / {p.Total/1000000:N0} MB");}),cancel.Token);
            if(disposed)return;
            var capture=new WaveInEvent{DeviceNumber=device,WaveFormat=new WaveFormat(16000,16,1),BufferMilliseconds=100};input=capture;
            capture.DataAvailable+=(_,e)=>{lock(gate){if(disposed||stopping)return;var length=Math.Min(e.BytesRecorded,3840000-(int)audio.Length);if(length>0)audio.Write(e.Buffer,0,length);double sum=0;for(var i=0;i+1<length;i+=2){var s=BitConverter.ToInt16(e.Buffer,i)/32768d;sum+=s*s;}var rms=length>0?Math.Sqrt(sum/(length/2)):0;dispatcher.BeginInvoke(new Action(()=>{if(!disposed)Level?.Invoke(Math.Min(1,rms*8));}));}};
            capture.RecordingStopped+=(_,e)=>dispatcher.BeginInvoke(new Action(async()=>{if(disposed)return;capture.Dispose();input=null;if(e.Exception!=null){Finish(e.Exception.Message);return;}await Transcribe();}));
            capture.StartRecording();limit.Start();Progress?.Invoke("● Recording… speak, then click Stop. Audio stays local.");
        }
        catch(OperationCanceledException){if(!disposed)Finish(null);}
        catch(Exception e){if(!disposed)Finish("Voice could not start: "+e.Message);}
    }
    public void Stop(){limit.Stop();if(disposed||stopping)return;stopping=true;if(input!=null){Progress?.Invoke("Transcribing locally…");input.StopRecording();}else{cancel.Cancel();Finish(null);}}
    private async Task Transcribe()
    {
        float[] samples;lock(gate){samples=PcmSamples(audio.ToArray());audio.SetLength(0);}
        try
        {
            Progress?.Invoke("Transcribing locally…");
            var text=await TranscribeSamples(samples,model!,cancel.Token);if(!disposed&&text.Length>0)Recognized?.Invoke(text);
            if(!disposed)Finish(null);
        }
        catch(OperationCanceledException){if(!disposed)Finish(null);}
        catch(Exception e){if(!disposed)Finish("Transcription failed: "+e.Message);}
        finally{Array.Clear(samples);}
    }
    internal static float[] PcmSamples(byte[] bytes){var samples=new float[bytes.Length/2];for(var i=0;i<samples.Length;i++)samples[i]=BitConverter.ToInt16(bytes,i*2)/32768f;Array.Clear(bytes);return samples;}
    internal static bool HasSpeech(float[] samples)=>samples.Length>=8000&&samples.Count(s=>Math.Abs(s)>0.007f)>1600;
    internal static async Task<string> TranscribeSamples(float[] samples,string model,CancellationToken token)
    {
        if(!HasSpeech(samples))return "";
        return await Task.Run(async()=>
        {
            await using var file=new FileStream(model,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
            if(!Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(WhisperVoiceAssets.Package.Artifacts[0].Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Voice model checksum failed. Repair voice setup before retrying.");file.Position=0;
            using var factory=WhisperFactory.FromPath(model);using var processor=factory.CreateBuilder().WithLanguage("en").WithThreads(Math.Clamp(Environment.ProcessorCount/2,2,8)).Build();
            var text=new System.Text.StringBuilder();await foreach(var segment in processor.ProcessAsync(samples,token)){if(text.Length>0)text.Append(' ');text.Append(segment.Text.Trim());if(text.Length>=16000)break;}return text.ToString()[..Math.Min(text.Length,16000)];
        },token);
    }
    private void Finish(string? error){Dispose();Completed?.Invoke(error);}
    public void Dispose(){if(disposed)return;disposed=true;limit.Stop();cancel.Cancel();var capture=input;input=null;capture?.Dispose();lock(gate){if(audio.TryGetBuffer(out var buffer)&&buffer.Array!=null)Array.Clear(buffer.Array);audio.Dispose();}}
}
