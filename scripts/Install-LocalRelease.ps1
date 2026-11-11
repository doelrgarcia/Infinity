param(
    [Parameter(Mandatory)][string]$VerificationReport,
    [Parameter(Mandatory)][string]$ConfigurationPath,
    [string]$DestinationRoot=(Join-Path $env:LOCALAPPDATA 'Programs\Infinity')
)
$ErrorActionPreference='Stop'
$report=Get-Content -LiteralPath $VerificationReport -Raw|ConvertFrom-Json
if($report.passed -ne $true -or $report.sourceRevision -notmatch '^[a-f0-9]{40}$'){throw 'Install requires a passing immutable-commit verification report.'}
$source=[IO.Path]::GetFullPath((Join-Path $report.work 'app'))
$base=[IO.Path]::GetFullPath($DestinationRoot)
$destination=Join-Path $base ('releases\'+$report.sourceRevision)
function Assert-NoLinks([string]$path){
    for($part=$path;$part;$part=[IO.Path]::GetDirectoryName($part)){
        if((Test-Path -LiteralPath $part) -and (Get-Item -LiteralPath $part -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Linked install paths are excluded.'}
    }
}
Assert-NoLinks $source
Assert-NoLinks $destination
if(!(Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)){throw 'Existing private configuration is required; it is not synthesized or removed.'}
$configHash=(Get-FileHash -LiteralPath $ConfigurationPath -Algorithm SHA256).Hash
$contract=Join-Path $report.work 'scripts\verification_contract.json'
$record=@($report.sourceFiles|Where-Object path -eq 'scripts/verification_contract.json')
if($record.Count -ne 1 -or (Get-FileHash -LiteralPath $contract).Hash -ne $record[0].sha256){throw 'Verification contract changed.'}
$expected=((Get-Content -LiteralPath $contract -Raw|ConvertFrom-Json).psobject.Properties|ForEach-Object {$_.Value.Count}|Measure-Object -Sum).Sum
if($report.assertions -ne $expected){throw 'Verification report has an incomplete assertion count.'}
$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($file in $report.packageFiles){
    if(!$names.Add($file.path)){throw 'Duplicate package file.'}
    $path=[IO.Path]::GetFullPath((Join-Path $source $file.path))
    $target=[IO.Path]::GetFullPath((Join-Path $destination $file.path))
    if(!$path.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -ieq 'appsettings.json'){throw 'Unsafe package member.'}
    Assert-NoLinks $path
    Assert-NoLinks $target
    if((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256){throw 'Verified source package changed.'}
}
foreach($name in @('LocalAI.Desktop.exe','LocalAI.Desktop.dll','LocalAI.Core.dll','Native\Infinity.RyzenHost.exe')){if(!$names.Contains($name)){throw 'Incomplete application package.'}}
if((Get-Item -LiteralPath (Join-Path $source 'LocalAI.Desktop.exe')).VersionInfo.ProductVersion -notlike ('*'+$report.sourceRevision+'*')){throw 'Application binary provenance differs.'}
foreach($file in $report.packageFiles){
    $target=Join-Path $destination $file.path
    if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256){throw 'Existing immutable installation differs. It was preserved.'};continue}
    New-Item -ItemType Directory -Force -Path (Split-Path $target)|Out-Null
    Copy-Item -LiteralPath (Join-Path $source $file.path) -Destination $target
    if((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256){throw 'Installed file verification failed; launcher remains unchanged.'}
}
$configTarget=Join-Path $destination 'appsettings.json'
Assert-NoLinks $configTarget
if(!(Test-Path -LiteralPath $configTarget)){Copy-Item -LiteralPath $ConfigurationPath -Destination $configTarget}
if((Get-FileHash -LiteralPath $ConfigurationPath).Hash -ne $configHash){throw 'Configuration changed during installation; review before switching launcher.'}
if((Get-FileHash -LiteralPath $configTarget).Hash -ne $configHash){throw 'Existing release configuration differs from the selected configuration. Both were preserved; launcher remains unchanged.'}
$launcher=Join-Path $base 'Start.cmd'
Assert-NoLinks $launcher
$backup=$null
if(Test-Path -LiteralPath $launcher){$backup=$launcher+'.'+[Guid]::NewGuid().ToString('N')+'.backup';Copy-Item -LiteralPath $launcher -Destination $backup}
$temporary=$launcher+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
[IO.File]::WriteAllText($temporary,"@echo off`r`nstart `"`" `""+('%~dp0releases\'+$report.sourceRevision+'\LocalAI.Desktop.exe')+"`" %*`r`n",[Text.Encoding]::ASCII)
Move-Item -LiteralPath $temporary -Destination $launcher -Force
[pscustomobject]@{SourceRevision=$report.sourceRevision;Destination=$destination;PackageFiles=$report.packageFiles.Count;ConfigurationSourcePreserved=$true;InstalledConfigurationMatchesSource=((Get-FileHash -LiteralPath $configTarget).Hash -eq $configHash);Launcher=$launcher;LauncherBackup=$backup;PublicSigning=$false}|ConvertTo-Json
