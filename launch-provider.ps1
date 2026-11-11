param([Parameter(Mandatory=$true)][ValidateSet('codex','cursor','grok','deepseek')][string]$Provider,[string]$Executable,[string]$ModelPath,[ValidateSet('Launch','Version','Help')][string]$Action='Launch',[switch]$NoOpen)
$ErrorActionPreference='Stop'
if($Provider -eq 'deepseek'){
    if(!$Executable){
        $candidates=@((Join-Path $env:LOCALAPPDATA 'Programs\DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'),(Join-Path $env:ProgramFiles 'DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'),(Join-Path $env:APPDATA 'npm\dsh.cmd'))
        $Executable=$candidates | Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} | Select-Object -First 1
    }
    if(!$Executable -or !(Test-Path -LiteralPath $Executable -PathType Leaf)){throw 'DeepSeek CLI not found. Install DeepSeek Harness from https://www.deepseek.com/en/download/.'}
    if($Action -eq 'Version'){& $Executable --version;exit $LASTEXITCODE}
    if($Action -eq 'Help'){& $Executable web --help;exit $LASTEXITCODE}
    $workspace=Join-Path $env:LOCALAPPDATA 'Rymaris/LocalAI/cli-bridge/deepseek'
    New-Item -ItemType Directory -Force -Path $workspace | Out-Null
    Set-Location -LiteralPath $workspace
    Write-Host 'DeepSeek CLI starts its local browser UI. No Infinity prompt or source files were passed. Review permissions in DeepSeek before granting access.'
    $deepSeekArguments=@('web','--host','127.0.0.1','--port','0')
    if($NoOpen){$deepSeekArguments+='--no-open'}
    & $Executable @deepSeekArguments
    exit $LASTEXITCODE
}
if($Executable){$providerConfig=[pscustomobject]@{Executable=$Executable;ModelPath=$ModelPath}}else{$configuration=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'appsettings.json') -Raw | ConvertFrom-Json; $providerConfig=$configuration.Models | Where-Object Engine -eq $Provider | Select-Object -First 1}
if($null -eq $providerConfig -or -not (Test-Path -LiteralPath $providerConfig.Executable)){throw 'Provider CLI not found. Check appsettings.json.'}
$workspace=Join-Path $env:LOCALAPPDATA "Rymaris/LocalAI/cli-bridge/$Provider"
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
Set-Location -LiteralPath $workspace
Write-Host 'Interactive provider CLI. No LocalAI prompt or source files were passed.'
if($Provider -eq 'cursor'){& $providerConfig.Executable $providerConfig.ModelPath}else{& $providerConfig.Executable}
