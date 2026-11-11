@echo off
cd /d "%~dp0"
if exist "%LOCALAPPDATA%\Programs\Infinity\Start.cmd" (
  call "%LOCALAPPDATA%\Programs\Infinity\Start.cmd"
  exit /b
)
if exist "%LOCALAPPDATA%\Programs\Infinity\LocalAI.Desktop.exe" (
  start "" "%LOCALAPPDATA%\Programs\Infinity\LocalAI.Desktop.exe"
  exit /b 0
)
if exist "publish\LocalAI.Desktop.exe" goto launch
if not defined INFINITY_NATIVE_BACKEND_DIRECTORY (
  echo Infinity requires a complete published package, including its native hybrid helper.
  echo For a source build, follow native\README.md and set INFINITY_NATIVE_BACKEND_DIRECTORY.
  exit /b 2
)
dotnet publish LocalAI.Desktop\LocalAI.Desktop.csproj -c Release -r win-x64 -o publish --self-contained true "-p:InfinityNativeBackendDirectory=%INFINITY_NATIVE_BACKEND_DIRECTORY%"
if errorlevel 1 exit /b 1
:launch
start "" "%~dp0publish\LocalAI.Desktop.exe"
