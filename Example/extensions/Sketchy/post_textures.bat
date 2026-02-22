@echo off
setlocal EnableExtensions DisableDelayedExpansion
echo [Sketchy Hook] executing post_textures.bat

set "_EXT_DIR=%~dp0"
set "_PROJ_DIR=%_EXT_DIR%..\.."
set "_BIN_EXE=%_EXT_DIR%Sketchy.exe"

if not exist "%_BIN_EXE%" (
	echo [Sketchy Hook] ERROR: Sketchy.exe not found at "%_BIN_EXE%"
	exit /b 1
)

echo [Sketchy Hook] running Sketchy.exe --post
"%_BIN_EXE%" "%_PROJ_DIR%" --post
set "_EXITCODE=%ERRORLEVEL%"
if not "%_EXITCODE%"=="0" (
	echo [Sketchy Hook] ERROR: Sketchy.exe --post failed with exit code %_EXITCODE%
	exit /b %_EXITCODE%
)

exit /b 0
