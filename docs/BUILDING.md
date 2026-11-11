# Build and test Infinity

Use Windows x64 with Git, Python 3.12+, PowerShell 7, .NET 10 SDK (see global.json), Visual Studio 2022 C++ build tools, and CMake. Public source includes no models, accounts, or private settings.

Work in your checkout. Keep generated output in an owned temporary folder:

```powershell
$work = Join-Path C:\tmp ('Infinity-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$env:TEMP = $work
$env:TMP = $work
$env:PYTHONDONTWRITEBYTECODE = '1'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
python native/prepare_inputs.py --directory "$work/native-inputs" --download
python native/build_backend.py --inputs "$work/native-inputs" --output "$work/native-output" --revision (git rev-parse HEAD)
dotnet publish LocalAI.Desktop -c Release -r win-x64 --self-contained true --artifacts-path "$work/artifacts" -o "$work/app" "-p:InfinityNativeBackendDirectory=$work/native-output"
& "$work/app/LocalAI.Desktop.exe"
```

The public example configuration is copied to the output as `appsettings.json` when a private root configuration is absent. To customize a source checkout, copy `appsettings.example.json` to `appsettings.json` and edit your copy. Never publish it. Installed configuration is beside the application; user data uses the compatibility path in the README. Restart after configuration changes.

## Offline checks

```powershell
python scripts/test_verify_release.py
python scripts/test_build_msi.py
dotnet run --project LocalAI.FileTests -c Release --artifacts-path "$work/file-tests" -- "$work/files.json"
dotnet run --project LocalAI.CommandTests -c Release --artifacts-path "$work/command-tests" -- "$work/commands.json"
dotnet run --project LocalAI.SourceTests -c Release --artifacts-path "$work/source-tests" -- "$work/source.json"
```

For the full immutable release gate, use `scripts/verify_release.py --ref HEAD --native-inputs <pinned-input-folder> --node <pinned-node.exe> --output <report.json>`. It requires a signed-in interactive Windows desktop for WPF/WebView2 suites. It exports the exact commit, uses isolated synthetic configuration/data, builds the native helper, and validates every named assertion against `scripts/verification_contract.json`. The required Node fixture SHA-256 is pinned in the verifier; extract the matching `dist-package/node.exe` from the pinned Cursor package in LocalAI.Core/dependency-catalog.json. No live provider/account requests are part of this gate.

Hosted CI runs only suitable offline checks and a source build; it does not claim interactive desktop coverage. Installer construction uses `scripts/build_msi.py` and requires a passing immutable report plus the matching WiX 7 UI extension. Retain its license and corresponding source under installer/third-party. See native/README.md for pinned inputs and `--help` on each release script for exact arguments.

Remove only your own obsolete generated files after preserving required evidence. Never remove app settings, backups, user data, models, or another task's files.
