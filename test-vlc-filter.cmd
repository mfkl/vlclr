@echo off
echo Launching VLC...
"C:\Users\Martin\Code\videolabs\vlclr\vlc-binaries\vlc-4.0.0-dev\vlc.exe" -vvv --no-hw-dec --video-filter=dotnet_overlay --play-and-exit "file:///C:/Users/Martin/Videos/BigBuckBunny.mp4" 2>&1
echo Done.
