@echo off
setlocal EnableExtensions DisableDelayedExpansion

echo [Sketchy Root Hook] executing pre_project_step.bat

set "_PROJ_DIR=%~dp0"
rem Remove trailing backslash to avoid Windows argv quoting edge case
if "%_PROJ_DIR:~-1%"=="\" set "_PROJ_DIR=%_PROJ_DIR:~0,-1%"

set "_BIN=%~dp0extensions\Sketchy\Sketchy.exe"

echo [Sketchy Root Hook] proj_dir="%_PROJ_DIR%"
echo [Sketchy Root Hook] bin="%_BIN%"

if not exist "%_BIN%" goto :missing_exe

"%_BIN%" "%_PROJ_DIR%" --pre
set "_EXITCODE=%ERRORLEVEL%"
if not "%_EXITCODE%"=="0" goto :tool_failed

exit /b 0

:missing_exe
echo Sketchy ERROR: Sketchy.exe not found.
echo Sketchy ERROR: Expected at: "%_BIN%"
exit /b 1

:tool_failed
echo Sketchy ERROR: Sketchy.exe --pre failed (exit code %_EXITCODE%).
exit /b %_EXITCODE%
