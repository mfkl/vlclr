@echo off
setlocal

set "VLC_INCLUDE=%~1"
if not defined VLC_INCLUDE set "VLC_INCLUDE=%~dp0..\..\vlc\include"
for %%I in ("%VLC_INCLUDE%") do set "VLC_INCLUDE=%%~fI"

if not exist "%VLC_INCLUDE%\vlc_common.h" (
    echo ERROR: vlc_common.h was not found under "%VLC_INCLUDE%".
    exit /b 2
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: vswhere.exe was not found at "%VSWHERE%".
    exit /b 3
)

set "VS_INSTALLATION="
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_INSTALLATION=%%I"
if not defined VS_INSTALLATION (
    echo ERROR: A Visual Studio installation with the C++ toolchain was not found.
    exit /b 4
)

call "%VS_INSTALLATION%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b %errorlevel%

set "ABI_OBJECT=%TEMP%\vlclr_abi_probe_%RANDOM%.obj"
cl /nologo /std:c++17 /TP /W0 /c /I"%VLC_INCLUDE%" "%~dp0vlclr_abi.cpp" /Fo"%ABI_OBJECT%"
exit /b %errorlevel%
