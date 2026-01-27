@echo off
echo Launching VLC...
echo Note: Edit VIDEO path below or pass as argument
set VIDEO=%1
if "%VIDEO%"=="" set VIDEO=file:///%USERPROFILE:\=/%/Videos/BigBuckBunny.mp4
"%~dp0vlc-binaries\vlc-4.0.0-dev\vlc.exe" -vvv --no-hw-dec --video-filter=dotnet_overlay --play-and-exit "%VIDEO%" 2>&1
echo Done.
