@echo off

:: Enable ANSI support for colors in newer Windows versions
setlocal enabledelayedexpansion

:: Color definitions (note: not all Windows cmd environments support these)
set "RED=[31m"
set "GREEN=[32m"
set "YELLOW=[33m"
set "BLUE=[34m"
set "NC=[0m"  :: No Color

:: Function to print messages in color
call :echo_message "%BLUE%" "[ INFO ] Running dotnet publish..."
dotnet publish -f net8.0 -c Release
set exit_code=%ERRORLEVEL%

:: Check if dotnet publish was successful
if %exit_code% neq 0 (
    call :echo_message "%RED%" "[ ERROR ] dotnet publish failed with exit code %exit_code%"
    exit /b 1
) else (
    call :echo_message "%GREEN%" "[ INFO ] dotnet publish completed successfully"
)

:: Delete deploy folder if exists
call :echo_message "%BLUE%" "[ INFO ] Deleting existing deploy folders..."
if exist Zenith\plugins rd /s /q Zenith\plugins
if exist Zenith\shared rd /s /q Zenith\shared

:: Create deploy folders
call :echo_message "%BLUE%" "[ INFO ] Creating deploy folders..."
md Zenith\plugins
md Zenith\shared

:: Copy main plugin folder to plugins folder, ignoring specific file names
call :echo_message "%YELLOW%" "[ INFO ] Copying main plugin files..."
robocopy src\bin\K4-Zenith\plugins\K4-Zenith Zenith\plugins\K4-Zenith /mir /xf "K4-ZenithAPI.dll" "KitsuneMenu.dll" "KitsuneMenu.pdb" /NDL /NFL /NJH /NJS /NP >nul

:: Copy shared folder's dirs to shared folder
call :echo_message "%YELLOW%" "[ INFO ] Copying shared files..."
robocopy src\bin\K4-Zenith\shared Zenith\shared /mir /NDL /NFL /NJH /NJS /NP >nul

:: Copy modules to plugins folder
call :echo_message "%YELLOW%" "[ INFO ] Copying TimeStats module..."
robocopy modules\time-stats\bin\K4-Zenith-TimeStats Zenith\plugins\K4-Zenith-TimeStats /mir /xf "KitsuneMenu.dll" "KitsuneMenu.pdb" /NDL /NFL /NJH /NJS /NP >nul

call :echo_message "%YELLOW%" "[ INFO ] Copying Ranks module..."
robocopy modules\ranks\bin\K4-Zenith-Ranks Zenith\plugins\K4-Zenith-Ranks /mir /NDL /NFL /NJH /NJS /NP >nul

call :echo_message "%YELLOW%" "[ INFO ] Copying Statistics module..."
robocopy modules\statistics\bin\K4-Zenith-Stats Zenith\plugins\K4-Zenith-Stats /mir /NDL /NFL /NJH /NJS /NP >nul

call :echo_message "%YELLOW%" "[ INFO ] Copying Admin module..."
rsync -a --quiet --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./modules/zenith-bans/bin/K4-Zenith-Bans/ ./Zenith/plugins/K4-Zenith-Bans/

call :echo_message "%YELLOW%" "[ INFO ] Copying Extended Commands module..."
rsync -a --quiet ./modules/extended-commands/bin/K4-Zenith-ExtendedCommands/ ./Zenith/plugins/K4-Zenith-ExtendedCommands/

call :echo_message "%YELLOW%" "[ INFO ] Copying Custom Tags module..."
rsync -a --quiet --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./modules/custom-tags/bin/K4-Zenith-CustomTags/ ./Zenith/plugins/K4-Zenith-CustomTags/

:: Delete files with a specific extension from Zenith and sub-folders
call :echo_message "%BLUE%" "[ INFO ] Cleaning up unnecessary files..."
for /r Zenith %%f in (*.pdb *.yaml .DS_Store) do del "%%f" 2>nul

call :echo_message "%GREEN%" "[ INFO ] Deployment completed successfully!"
exit /b 0

:echo_message
    echo %~2
    goto :eof
