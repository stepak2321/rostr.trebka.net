@echo off
setlocal
set "HERE=%~dp0"
if exist "%HERE%RobloxLauncher.csproj" (
    set "PROJ=%HERE%"
) else (
    set "PROJ=%HERE%RobloxLauncher\"
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

rem ---- Ask for the version. It is locked into the exe, and it must match the GitHub release tag (v + this number).
:askversion
set "VER="
set /p "VER=What version is this build? (three numbers, for example 1.4.0): "
if not defined VER goto askversion
set "VER=%VER: =%"
echo %VER%| findstr /r /x "[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*" >nul
if errorlevel 1 (
    echo That is not a version. Use three numbers like 1.4.0
    goto askversion
)
echo.

taskkill /f /im Rostr.exe >nul 2>&1
taskkill /f /im RobloxLauncher.exe >nul 2>&1

if exist "%HERE%dist\Rostr.exe" del /f /q "%HERE%dist\Rostr.exe"
if exist "%HERE%dist\Rostr.exe.sha256" del /f /q "%HERE%dist\Rostr.exe.sha256"
if exist "%HERE%dist\RobloxLauncher.exe" del /f /q "%HERE%dist\RobloxLauncher.exe"

echo Building Rostr.exe version %VER% ...
echo (self-contained: users do NOT need to install .NET. The first build downloads the
echo  runtime from NuGet, so this PC needs internet once.)
echo.
dotnet publish "%PROJ%RobloxLauncher.csproj" -c Release -r win-x64 --self-contained true ^
    -p:Version=%VER% -p:AssemblyVersion=%VER%.0 -p:FileVersion=%VER%.0 -p:InformationalVersion=%VER% ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None -p:DebugSymbols=false ^
    -o "%HERE%dist"
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Scroll up to read the error.
    pause
    exit /b 1
)

if not exist "%HERE%dist\Rostr.exe" (
    echo.
    echo BUILD FAILED: dist\Rostr.exe was not created.
    pause
    exit /b 1
)

rem ---- Show the version that is really inside the exe, and write a checksum file as a backup for the updater.
set "EXEFILE=%HERE%dist\Rostr.exe"
for /f "delims=" %%V in ('powershell -NoProfile -Command "(Get-Item -LiteralPath $env:EXEFILE).VersionInfo.FileVersion"') do echo Version locked into the exe: %%V
powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 -LiteralPath $env:EXEFILE).Hash.ToLower() | Out-File -Encoding ascii -LiteralPath ($env:EXEFILE + '.sha256')"

echo.
echo ============================================================
echo  Done.  Rostr %VER%  -  %HERE%dist\Rostr.exe
echo.
echo  To publish it: GitHub - Releases - Draft a new release
echo     Tag:    v%VER%
echo     Attach: dist\Rostr.exe   (and dist\Rostr.exe.sha256 if you like)
echo  Rostr checks the newest release by itself and offers the update.
echo ============================================================
explorer "%HERE%dist"
pause
