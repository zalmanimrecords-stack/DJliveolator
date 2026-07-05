<#
.SYNOPSIS
    Build the Liveolator YouTube promo clip from the canonical UI-shot captures.

.DESCRIPTION
    Assembles artifacts/ui-shots/*.png into a 1080p marketing video: title card,
    one captioned slide per feature (with a slow Ken Burns zoom), and an outro
    with the download URL. Background music is synthesized with ffmpeg (124 BPM
    electronic loop) unless -Music points at a real track.

    Re-capture the shots first if they are stale:
        dotnet test tests/Liveolator.App.Tests --filter UiShots
    (copy bass*.dll from src/Liveolator.App/bin/Debug/net8.0 into the test output
    first, or the shots render with the "audio engine unavailable" banner).

.PARAMETER Music
    Optional path to an audio file to use instead of the synthesized loop.

.PARAMETER OutFile
    Output mp4 path. Default: artifacts/marketing/liveolator-promo-youtube.mp4
#>
[CmdletBinding()]
param(
    [string]$Music = '',
    [string]$OutFile = '',
    [string]$FfmpegExe = 'C:\ffmpeg\bin\ffmpeg.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$shotsDir = Join-Path $repoRoot 'artifacts/ui-shots'
$outDir   = Join-Path $repoRoot 'artifacts/marketing'
if (-not $OutFile) { $OutFile = Join-Path $outDir 'liveolator-promo-youtube.mp4' }
$workDir  = Join-Path $outDir 'promo-work'
New-Item -ItemType Directory -Force -Path $outDir, $workDir | Out-Null

if (-not (Test-Path $FfmpegExe)) { throw "ffmpeg not found at $FfmpegExe" }

$fps   = 25
$bg    = '0x05070b'
$gold  = '0xf5d9a0'
# drawtext needs the drive colon escaped inside the filter string
$font     = 'C\:/Windows/Fonts/segoeuib.ttf'
$fontBig  = 'C\:/Windows/Fonts/segoeuib.ttf'

# --- Slide list: image (relative to ui-shots) + caption ------------------------
$slides = @(
    @{ img = '00-LIVE.png';               cap = 'Two decks - mixer - visuals. One screen. One beat.' }
    @{ img = 'promo-console.png';         cap = 'Two decks - full mixer - one shared beat clock' }
    @{ img = 'promo-deck.png';            cap = 'Hands-on deck control - hot cues - loops - key lock - pitch' }
    @{ img = 'promo-waveform.png';        cap = '3-band waveforms with kick-forward glow - read the mix at a glance' }
    @{ img = '02-STUDIO.png';             cap = 'STUDIO - plan a full set on a DAW-style timeline and render it' }
    @{ img = 'jog-medusa.png';            cap = 'Beat-lit jog rings - position - loop and sync in real time' }
    @{ img = 'control-skins-applied.png'; cap = 'Every knob and fader is skinnable - make the rig yours' }
    @{ img = '04-LIBRARIES.png';          cap = 'Auto analysis - BPM - key - grid - cues. Imports from Rekordbox - Serato - Traktor and more' }
)

$slideDur = 5.6   # seconds per content slide
$titleDur = 4.5
$outroDur = 5.5
$frames   = [int]($slideDur * $fps)

# Start-Process keeps ffmpeg's stderr chatter out of PowerShell's error stream
# (PS 5.1 wraps redirected native stderr in NativeCommandError records).
function Invoke-Ffmpeg([string[]]$ffArgs, [string]$what) {
    $log = Join-Path $workDir 'ffmpeg.log'
    $quoted = $ffArgs | ForEach-Object { '"{0}"' -f ($_ -replace '"','\"') }
    $p = Start-Process -FilePath $FfmpegExe -ArgumentList (@('-hide_banner','-loglevel','error') + $quoted) `
        -NoNewWindow -Wait -PassThru -RedirectStandardError $log
    if ($p.ExitCode -ne 0) {
        Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 15 | Write-Host
        throw "ffmpeg failed ($what)."
    }
}

# --- Title + outro cards --------------------------------------------------------
Write-Host 'Rendering title and outro cards...' -ForegroundColor Cyan
$titleVf = "drawtext=fontfile='${fontBig}':text='LIVEOLATOR':fontcolor=${gold}:fontsize=150:x=(w-text_w)/2:y=(h-text_h)/2-70," +
           "drawtext=fontfile='${font}':text='DJ + VJ performance - locked to one beat':fontcolor=0xd8dbe2:fontsize=52:x=(w-text_w)/2:y=(h)/2+90," +
           "fade=t=in:st=0:d=0.6,fade=t=out:st=$($titleDur-0.5):d=0.5,format=yuv420p"
Invoke-Ffmpeg @('-y','-f','lavfi','-i',"color=c=${bg}:s=1920x1080:d=${titleDur}:r=${fps}",
    '-vf',$titleVf,'-c:v','libx264','-crf','18','-preset','medium',(Join-Path $workDir 'seg-00-title.mp4')) 'title card'

$outroVf = "drawtext=fontfile='${fontBig}':text='LIVEOLATOR':fontcolor=${gold}:fontsize=130:x=(w-text_w)/2:y=(h-text_h)/2-120," +
           "drawtext=fontfile='${font}':text='Free download for Windows':fontcolor=0xd8dbe2:fontsize=54:x=(w-text_w)/2:y=(h)/2+40," +
           "drawtext=fontfile='${font}':text='liveolator.zalmanim.com':fontcolor=${gold}:fontsize=64:x=(w-text_w)/2:y=(h)/2+140," +
           "fade=t=in:st=0:d=0.5,fade=t=out:st=$($outroDur-0.8):d=0.8,format=yuv420p"
Invoke-Ffmpeg @('-y','-f','lavfi','-i',"color=c=${bg}:s=1920x1080:d=${outroDur}:r=${fps}",
    '-vf',$outroVf,'-c:v','libx264','-crf','18','-preset','medium',(Join-Path $workDir 'seg-99-outro.mp4')) 'outro card'

# --- Content slides: fit on canvas, slow zoom, caption, fade --------------------
$i = 1
foreach ($s in $slides) {
    $img = Join-Path $shotsDir $s.img
    if (-not (Test-Path $img)) { Write-Warning "Missing shot $($s.img) - skipped."; continue }
    Write-Host "Rendering slide $i - $($s.img)" -ForegroundColor Cyan

    # captions go through textfile= to dodge filter-escaping of punctuation
    $capFile = Join-Path $workDir "cap-$i.txt"
    [IO.File]::WriteAllText($capFile, $s.cap, (New-Object Text.UTF8Encoding($false)))
    $capPath = ($capFile -replace '\\','/') -replace ':','\:'

    $fadeOut = $slideDur - 0.5
    # image sits above center so the caption band never covers UI
    $vf = "scale=2880:1380:force_original_aspect_ratio=decrease," +
          "pad=2880:1620:(ow-iw)/2:(oh-ih)/2-100:color=$bg," +
          "zoompan=z='1+0.07*on/$frames':d=${frames}:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1920x1080:fps=$fps," +
          "drawtext=fontfile='${font}':textfile='${capPath}':fontcolor=${gold}:fontsize=44:x=(w-text_w)/2:y=h-130:box=1:boxcolor=$bg@0.72:boxborderw=18," +
          "fade=t=in:st=0:d=0.5,fade=t=out:st=${fadeOut}:d=0.5,format=yuv420p"
    Invoke-Ffmpeg @('-y','-i',$img,'-vf',$vf,'-c:v','libx264','-crf','18','-preset','medium','-r',"$fps",
        (Join-Path $workDir ("seg-{0:00}-slide.mp4" -f $i))) "slide $i"
    $i++
}

# --- Background music -----------------------------------------------------------
$totalDur = [math]::Round($titleDur + ($i - 1) * $slideDur + $outroDur, 2)
$musicFile = Join-Path $workDir 'music.m4a'
if ($Music -and (Test-Path $Music)) {
    Write-Host "Using provided music: $Music" -ForegroundColor Cyan
    Invoke-Ffmpeg @('-y','-i',$Music,'-t',"$totalDur",'-af',"afade=t=in:st=0:d=0.5,afade=t=out:st=$($totalDur-2.5):d=2.5",
        '-c:a','aac','-b:a','192k',$musicFile) 'music trim'
} else {
    Write-Host 'Synthesizing 124 BPM backing loop...' -ForegroundColor Cyan
    # ponytail: kick + offbeat hat + eighth-note sub synthesized by expression;
    # pass -Music <file> for a real track.
    $beat = 0.483871   # 60 / 124
    $kick = "aevalsrc=exprs='0.85*sin(2*PI*(45+70*exp(-25*mod(t\,$beat)))*mod(t\,$beat))*exp(-9*mod(t\,$beat))':s=44100:d=$totalDur[k]"
    $hat  = "aevalsrc=exprs='0.10*(2*random(0)-1)*exp(-70*mod(t+$($beat/2)\,$beat))':s=44100:d=$totalDur[h0];[h0]highpass=f=7000[h]"
    $bass = "aevalsrc=exprs='0.22*sin(2*PI*if(lt(mod(t\,7.742)\,3.871)\,55\,65.4)*t)*exp(-10*mod(t\,$($beat/2)))':s=44100:d=$totalDur[b]"
    $mix  = "[k][h][b]amix=inputs=3:normalize=0,alimiter=limit=0.9,volume=0.8,afade=t=in:st=0:d=0.5,afade=t=out:st=$($totalDur-3):d=3[aud]"
    Invoke-Ffmpeg @('-y','-filter_complex',"$kick;$hat;$bass;$mix",'-map','[aud]','-c:a','aac','-b:a','192k',$musicFile) 'music synth'
}

# --- Concat + mux ----------------------------------------------------------------
Write-Host 'Concatenating segments...' -ForegroundColor Cyan
$listFile = Join-Path $workDir 'segments.txt'
$segs = Get-ChildItem $workDir -Filter 'seg-*.mp4' | Sort-Object Name
$lines = $segs | ForEach-Object { "file '$($_.FullName -replace '\\','/')'" }
[IO.File]::WriteAllLines($listFile, $lines, (New-Object Text.UTF8Encoding($false)))

Invoke-Ffmpeg @('-y','-f','concat','-safe','0','-i',$listFile,'-i',$musicFile,
    '-c:v','copy','-c:a','aac','-b:a','192k','-shortest','-movflags','+faststart',$OutFile) 'final mux'

$len = (Get-Item $OutFile).Length / 1MB
Write-Host ("Done: {0} ({1:N1} MB, ~{2}s)" -f $OutFile, $len, [int]$totalDur) -ForegroundColor Green
