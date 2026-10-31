@echo off
set "LAUNCHER_BAT=%~f0"
set "EXE=%~dp0dist\Rostr.exe"

if /i "%~1"=="dev" goto dev
if exist "%EXE%" (
    start "" "%EXE%" %1
    exit /b
)

:dev
if exist "%~dp0RobloxLauncher.csproj" (
    cd /d "%~dp0"
) else (
    cd /d "%~dp0RobloxLauncher"
)
if "%~1"=="" goto manual
if /i "%~1"=="dev" goto manual
dotnet run -c Release --no-build -- "%~1"
goto :eof
:manual
dotnet run -c Release
pause