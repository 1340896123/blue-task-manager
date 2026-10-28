@echo off
setlocal DisableDelayedExpansion
set "TASKMANAGER_RESTORE_HOST=%~dp0Takao.Host.exe"
set "restoreExitCode=2"
if not exist "%TASKMANAGER_RESTORE_HOST%" (
    echo ERROR: Takao.Host.exe is missing from this directory.
    echo Restore the complete application directory and try again.
    goto finish
)

echo Restoring the Windows standard Task Manager...
echo Approve the Windows administrator permission prompt to continue.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$ErrorActionPreference='Stop'; try { $process=Start-Process -FilePath $env:TASKMANAGER_RESTORE_HOST -ArgumentList '--uninstall-task-manager-integration' -Verb RunAs -Wait -PassThru; if ($process.ExitCode -ne 0) { throw ('The restore command failed with exit code ' + $process.ExitCode + '.'); }; $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64); try { $key=$registry.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'); if ($null -ne $key) { try { $names=$key.GetValueNames(); if ($names -contains 'Debugger') { throw 'Task Manager is still redirected by another configuration. Its Debugger was not removed.'; }; if (($names -contains 'UseFilter') -and (($key.GetValueKind('UseFilter') -ne [Microsoft.Win32.RegistryValueKind]::DWord) -or ($key.GetValue('UseFilter') -ne 0))) { throw 'Task Manager still has an active or invalid UseFilter configuration. Restoration could not be confirmed.'; }; } finally { $key.Dispose(); }; }; } finally { $registry.Dispose(); }; Write-Host 'Windows standard Task Manager is restored. Ctrl+Shift+Esc will use the Windows entry.'; exit 0; } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1; }"
set "restoreExitCode=%errorlevel%"
if not "%restoreExitCode%"=="0" echo ERROR: Restoration failed or was cancelled. Windows standard Task Manager was not confirmed.

:finish
echo.
if /i not "%~1"=="--no-pause" pause
exit /b %restoreExitCode%
