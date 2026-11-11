param([Parameter(Mandatory)][string]$ApplicationRoot,[Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('Infinity-install-tests-'+[Guid]::NewGuid().ToString('N'))
$work=Join-Path $fixture 'work'
$app=Join-Path $work 'app'
New-Item -ItemType Directory -Path $app,(Join-Path $work 'scripts') -Force|Out-Null
$revision=[regex]::Match((Get-Item (Join-Path $ApplicationRoot 'LocalAI.Desktop.exe')).VersionInfo.ProductVersion,'[a-f0-9]{40}').Value
if(!$revision){throw 'Supply a provenance-stamped existing application as the inert file fixture.'}
$files=@('LocalAI.Desktop.exe','LocalAI.Desktop.dll','LocalAI.Core.dll','Native\Infinity.RyzenHost.exe')
$package=@(foreach($file in $files){$target=Join-Path $app $file;New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $ApplicationRoot $file) -Destination $target;@{path=$file;sha256=(Get-FileHash $target).Hash}})
$contract=Join-Path $work 'scripts\verification_contract.json'
[IO.File]::WriteAllText($contract,'{"synthetic-fixture":["one"]}')
$report=Join-Path $fixture 'report.json'
@{passed=$true;sourceRevision=$revision;work=$work;assertions=1;sourceFiles=@(@{path='scripts/verification_contract.json';sha256=(Get-FileHash $contract).Hash});packageFiles=$package}|ConvertTo-Json -Depth 8|Set-Content $report
$config=Join-Path $fixture 'private-config.json'
[IO.File]::WriteAllText($config,'{"UserDataRoot":"C:\\synthetic-existing-store","fixture":"selected"}')
$sourceHash=(Get-FileHash $config).Hash
$installer=Join-Path $PSScriptRoot 'Install-LocalRelease.ps1'
$rows=[ordered]@{}
$fresh=Join-Path $fixture 'fresh'
$installed= & $installer -VerificationReport $report -ConfigurationPath $config -DestinationRoot $fresh | ConvertFrom-Json
$rows.freshInstallVerifiesAllFixtureFiles=$installed.PackageFiles -eq 4 -and @($package|Where-Object {(Get-FileHash (Join-Path $installed.Destination $_.path)).Hash -ne $_.sha256}).Count -eq 0
$rows.freshInstallPreservesSelectedConfiguration=$installed.InstalledConfigurationMatchesSource -and (Get-FileHash $config).Hash -eq $sourceHash
$again= & $installer -VerificationReport $report -ConfigurationPath $config -DestinationRoot $fresh | ConvertFrom-Json
$rows.matchingExistingConfigurationIsIdempotent=$again.InstalledConfigurationMatchesSource -and (Get-FileHash (Join-Path $again.Destination 'appsettings.json')).Hash -eq $sourceHash
$mismatch=Join-Path $fixture 'mismatch'
$existing=Join-Path $mismatch ('releases\'+$revision+'\appsettings.json')
New-Item -ItemType Directory -Path (Split-Path $existing) -Force|Out-Null
[IO.File]::WriteAllText($existing,'{"fixture":"previous"}')
$existingHash=(Get-FileHash $existing).Hash
$launcher=Join-Path $mismatch 'Start.cmd'
[IO.File]::WriteAllText($launcher,'preserve this launcher')
$launcherHash=(Get-FileHash $launcher).Hash
$rejected=$false
try{ & $installer -VerificationReport $report -ConfigurationPath $config -DestinationRoot $mismatch|Out-Null }catch{if($_.Exception.Message -notlike 'Existing release configuration differs*'){throw};$rejected=$true}
$rows.differentConfigurationStopsBeforeLauncherSwitch=$rejected -and (Get-FileHash $launcher).Hash -eq $launcherHash
$rows.bothConfigurationsRemainUnchanged=(Get-FileHash $existing).Hash -eq $existingHash -and (Get-FileHash $config).Hash -eq $sourceHash
$badPackage=Join-Path $fixture 'bad-package'
[IO.File]::AppendAllText((Join-Path $app 'LocalAI.Core.dll'),'fixture corruption')
$rejected=$false
try{ & $installer -VerificationReport $report -ConfigurationPath $config -DestinationRoot $badPackage|Out-Null }catch{if($_.Exception.Message -notlike 'Verified source package changed*'){throw};$rejected=$true}
$rows.changedPackageCannotSwitchLauncher=$rejected -and !(Test-Path (Join-Path $badPackage 'Start.cmd'))
# Replace only the disposable already-verified fixture with a tiny argument
# recorder. Exercise the actual generated launcher without opening any UI.
$probe=Join-Path $fixture 'argument-probe'
New-Item -ItemType Directory -Path $probe|Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'probe.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>WinExe</OutputType><AssemblyName>LocalAI.Desktop</AssemblyName><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $probe 'Program.cs'),'var output=Environment.GetEnvironmentVariable("INFINITY_LAUNCHER_TEST_OUTPUT"); if(output!=null)File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(args));')
[IO.File]::WriteAllText((Join-Path $probe 'NuGet.Config'),'<configuration><packageSources><clear /></packageSources></configuration>')
$probeOutput=Join-Path $probe 'output'
& dotnet build (Join-Path $probe 'probe.csproj') -c Release --artifacts-path (Join-Path $probe 'artifacts') -o $probeOutput --nologo *> (Join-Path $probe 'build.log')
if($LASTEXITCODE -ne 0){throw 'Could not build the inert launcher argument fixture.'}
foreach($file in Get-ChildItem -LiteralPath $probeOutput -File){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $installed.Destination $file.Name) -Force}
$argumentResult=Join-Path $fixture 'arguments.json'
$start=[Diagnostics.ProcessStartInfo]::new($env:ComSpec)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.Environment['INFINITY_LAUNCHER_TEST_OUTPUT']=$argumentResult
foreach($argument in @('/c',(Join-Path $fresh 'Start.cmd'),'--chat','space value','unicode-Ω')){$start.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::Start($start)
try{if(!$process.WaitForExit(15000)){throw 'Launcher argument fixture timed out.'}}finally{$process.Dispose()}
$deadline=[DateTime]::UtcNow.AddSeconds(15)
while(!(Test-Path -LiteralPath $argumentResult) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 50}
if(!(Test-Path -LiteralPath $argumentResult)){throw 'The inert launcher fixture did not produce its receipt.'}
$arguments=@(Get-Content -LiteralPath $argumentResult -Raw|ConvertFrom-Json)
$rows.launcherPreservesChatSpacedAndUnicodeArguments=$arguments.Count -eq 3 -and $arguments[0] -ceq '--chat' -and $arguments[1] -ceq 'space value' -and $arguments[2] -ceq 'unicode-Ω'
$rows.noRealApplicationOrProviderWasExecuted=$true
$rows|ConvertTo-Json|Set-Content -LiteralPath $ResultPath
$rows|ConvertTo-Json
if(@($rows.Values|Where-Object {$_ -ne $true}).Count){throw 'Installer regression check failed.'}
