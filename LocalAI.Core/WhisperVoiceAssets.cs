using System.Security.Cryptography;
namespace LocalAI.Core;
public static class WhisperVoiceAssets
{
    public static DependencyPackage Package=>new(){Id="whisper-small-en",Label="Local Whisper voice (English)",Version="q5-1-bfdff489",LicenseUrl="https://github.com/openai/whisper/blob/main/LICENSE",InstalledBytes=190098681,RequiredFiles=["model.bin"],Artifacts=[new(){Url="https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.en-q5_1.bin",Bytes=190098681,Sha256="bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30",Path="model.bin"}]};
    public static bool Installed{get{using var installer=new DependencyInstaller(StartupDependencies.DefaultRoot);return installer.IsInstalled(Package);}}
    public static async Task<string> Ensure(IProgress<DependencyProgress>? progress,CancellationToken token)
    {
        using var installer=new DependencyInstaller(StartupDependencies.DefaultRoot);var p=Package;var path=DependencyInstaller.SafePath(installer.PackagePath(p),"model.bin");var force=false;
        if(installer.IsInstalled(p)){await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);if(Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(p.Artifacts[0].Sha256,StringComparison.OrdinalIgnoreCase))return path;force=true;}
        await installer.Install(p,progress,token,force);return path;
    }
}
