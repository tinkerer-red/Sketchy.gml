@echo off
setlocal EnableExtensions DisableDelayedExpansion
echo [Sketchy Hook] executing post_clean_step.bat

set "_EXT_DIR=%~dp0"
set "_PROJ_DIR=%_EXT_DIR%..\.."
set "_BIN_EXE=%_EXT_DIR%Sketchy.exe"

if not exist "%_BIN_EXE%" (
	echo [Sketchy Hook] ERROR: Sketchy.exe not found at "%_BIN_EXE%"
	exit /b 1
)

echo [Sketchy Hook] running Sketchy.exe --clean
"%_BIN_EXE%" "%_PROJ_DIR%" --clean
set "_EXITCODE=%ERRORLEVEL%"
if not "%_EXITCODE%"=="0" (
	echo [Sketchy Hook] ERROR: Sketchy.exe --clean failed with exit code %_EXITCODE%
	exit /b %_EXITCODE%
)

exit /b 0
