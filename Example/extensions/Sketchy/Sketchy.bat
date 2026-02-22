\
@echo off
echo [Sketchy Hook] executing Sketchy.bat wrapper
setlocal EnableExtensions DisableDelayedExpansion
set "_EXT_DIR=%~dp0"
if exist "%_EXT_DIR%Sketchy.exe" (
    "%_EXT_DIR%Sketchy.exe" %*
    exit /b %ERRORLEVEL%
)
echo Sketchy ERROR: Sketchy.exe not found in "%_EXT_DIR%"
exit /b 1
