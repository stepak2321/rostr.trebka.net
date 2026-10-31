@echo off
setlocal
rem ------------------------------------------------------------------
rem  clean.bat - removes build output and leftovers from RobloxLauncher.
rem  Keeps ALL source files, including settings\FastFlagManager.cs.
rem  Put this file next to RobloxLauncher.csproj (or one folder above).
rem ------------------------------------------------------------------

set "HERE=%~dp0"
if exist "%HERE%RobloxLauncher.csproj" (
    set "PROJ=%HERE%"
) else if exist "%HERE%RobloxLauncher\RobloxLauncher.csproj" (
    set "PROJ=%HERE%RobloxLauncher\"
) else (
    echo Could not find RobloxLauncher.csproj next to this script.
    pause
    exit /b 1
)

echo Project folder: %PROJ%
echo.
echo This will DELETE:
echo   bin\   (build output + downloaded Roblox test client)
echo   obj\   (intermediate build files)
echo   .vs\   (Visual Studio cache, if present)
echo.
echo This will KEEP: all .cs / .xaml / .csproj / .bat files, the settings
echo folder (including FastFlagManager.cs) and the dist folder.
echo.
set /p "OK=Continue? (y/n): "
if /i not "%OK%"=="y" (
    echo Cancelled.
    exit /b 0
)

rem Make sure no running copy locks files in bin\
taskkill /f /im Rostr.exe >nul 2>&1
taskkill /f /im RobloxLauncher.exe >nul 2>&1

for %%D in (bin obj .vs) do (
    if exist "%PROJ%%%D" (
        echo Deleting %%D ...
        rmdir /s /q "%PROJ%%%D"
    )
)

echo.
echo Done. Source files were not touched.
pause