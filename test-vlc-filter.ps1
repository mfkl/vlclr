Write-Host "Starting..."
$cmd = 'C:\Users\Martin\Code\videolabs\vlclr\vlc-binaries\vlc-4.0.0-dev\vlc.exe -vvv --no-hw-dec --video-filter=dotnet_overlay --play-and-exit "file:///C:/Users/Martin/Videos/BigBuckBunny.mp4"'
Write-Host "Command: $cmd"
cmd /c $cmd 2>&1
Write-Host "Done."
