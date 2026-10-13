# Renders a synthetic 1080p59.94 pattern made to show combing to non-experts,
# then interlaces it to 1080i29.97 (top field first) like a broadcast.
#   - a caption that never moves (stays sharp with or without deinterlacing)
#   - large text scrolling at constant speed
#   - a picket fence of vertical bars moving the other way
#   - a ball swinging left and right: combing grows with speed
#
#   ./make-demo.ps1 [-Out <dir>] [-Seconds 12]
param(
    [string] $Out = 'C:\Users\Martin\Videos\vlclr-demo\yadif-source',
    [int] $Seconds = 12
)
$ErrorActionPreference = 'Stop'

# drawtext wants a plain relative font path; run ffmpeg next to a copy.
$work = Join-Path ([IO.Path]::GetTempPath()) 'vlclr-yadif-demo'
New-Item -ItemType Directory -Force $work, $Out | Out-Null
Copy-Item "$env:WINDIR\Fonts\arialbd.ttf" (Join-Path $work 'font.ttf') -Force

$graph = @"
color=c=0x14182b:s=1920x1080:r=60000/1001:d=$Seconds,format=yuv420p,
drawtext=fontfile=font.ttf:text='STILL TEXT STAYS SHARP':fontsize=64:fontcolor=0xb0b0b0:x=60:y=50,
drawtext=fontfile=font.ttf:text='MOVING TEXT':fontsize=230:fontcolor=white:x='mod(t*1200\,w+tw)-tw':y=170[bg];
color=c=0x14182b:s=2160x230:r=60000/1001:d=$Seconds,format=yuv420p,
drawgrid=w=120:h=ih+10:t=40:c=0xffd400[fence];
color=c=black:s=220x220:r=60000/1001:d=$Seconds,format=yuva420p,
geq=lum=150:cb=44:cr=200:a='if(lt(hypot(X-110\,Y-110)\,105)\,255\,0)'[ball];
[bg][fence]overlay=x='-mod(t*600\,120)':y=480[a];
[a][ball]overlay=x='(W-w)/2+800*sin(2*PI*t/3)':y=790:format=yuv420,
interlace=scan=tff:lowpass=0[out]
"@
$graphFile = Join-Path $work 'graph.txt'
Set-Content $graphFile $graph -NoNewline

$file = Join-Path $Out 'pattern-1080i.mp4'
Push-Location $work
try {
    ffmpeg -hide_banner -loglevel error -y -filter_complex_script graph.txt -map '[out]' `
        -c:v libx264 -preset medium -crf 10 -flags +ilme+ildct -x264-params tff=1 -pix_fmt yuv420p $file
    if ($LASTEXITCODE) { throw 'ffmpeg failed' }
}
finally { Pop-Location }
ffprobe -v error -select_streams v -show_entries stream=width,height,r_frame_rate,field_order -of default=nw=1 $file
Get-Item $file | Select-Object FullName, Length
