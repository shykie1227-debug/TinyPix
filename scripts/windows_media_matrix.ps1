param(
  [string]$EngineDirectory = "$env:LOCALAPPDATA\TinyPixBuild\src-tauri\resources",
  [string]$OutputDirectory = "$env:TEMP\TinyPixMediaMatrix"
)

$ErrorActionPreference = 'Stop'
$ffmpeg = Join-Path $EngineDirectory 'ffmpeg.exe'
$ffprobe = Join-Path $EngineDirectory 'ffprobe.exe'
if (!(Test-Path $ffmpeg) -or !(Test-Path $ffprobe)) { throw 'FFmpeg/FFprobe resources not found.' }

Remove-Item $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$sourceName = ('{0}{1} source.mp4' -f [char]0x4E2D, [char]0x6587)
$source = Join-Path $OutputDirectory $sourceName

& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=640x360:rate=24' -f lavfi -i 'sine=frequency=1000:sample_rate=48000' -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $source
if ($LASTEXITCODE -ne 0) { throw 'Failed to generate source video.' }

$jobs = @(
  @{ Name='video-mp4'; Path=('{0}{1} output.mp4' -f [char]0x8F93, [char]0x51FA); Args=@('-c:v','libx264','-c:a','aac') },
  @{ Name='video-mov'; Path='output.mov'; Args=@('-c:v','libx264','-c:a','aac') },
  @{ Name='video-mkv'; Path='output.mkv'; Args=@('-c:v','libx264','-c:a','aac') },
  @{ Name='video-avi'; Path='output.avi'; Args=@('-c:v','mpeg4','-c:a','libmp3lame') },
  @{ Name='video-webm'; Path='output.webm'; Args=@('-c:v','libvpx-vp9','-c:a','libopus') },
  @{ Name='audio-mp3'; Path='audio.mp3'; Args=@('-vn','-c:a','libmp3lame') },
  @{ Name='audio-wav'; Path='audio.wav'; Args=@('-vn','-c:a','pcm_s16le') },
  @{ Name='audio-aac'; Path='audio.aac'; Args=@('-vn','-c:a','aac') },
  @{ Name='audio-flac'; Path='audio.flac'; Args=@('-vn','-c:a','flac') }
)

$results = @()
foreach ($job in $jobs) {
  $output = Join-Path $OutputDirectory $job.Path
  & $ffmpeg -hide_banner -loglevel error -y -i $source @($job.Args) $output
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $output)) { throw "Failed: $($job.Name)" }
  $probe = & $ffprobe -v error -show_entries 'format=format_name,duration' -of json $output | ConvertFrom-Json
  $results += [ordered]@{ name=$job.Name; path=$output; bytes=(Get-Item $output).Length; format=$probe.format.format_name; duration=[double]$probe.format.duration; passed=$true }
}

$gif = Join-Path $OutputDirectory ('{0}{1}.gif' -f [char]0x52A8, [char]0x753B)
& $ffmpeg -hide_banner -loglevel error -y -ss 0.25 -i $source -t 1.5 -filter_complex 'fps=15,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff:max_colors=128[p];[b][p]paletteuse=dither=sierra2_4a' -loop 0 $gif
if ($LASTEXITCODE -ne 0 -or !(Test-Path $gif)) { throw 'Failed: gif' }
$results += [ordered]@{ name='gif'; path=$gif; bytes=(Get-Item $gif).Length; passed=$true }

$lossless = Join-Path $OutputDirectory 'lossless-trim.mp4'
& $ffmpeg -hide_banner -loglevel error -y -i $source -ss 0.5 -t 1.5 -map 0 -c copy -avoid_negative_ts make_zero $lossless
if ($LASTEXITCODE -ne 0 -or !(Test-Path $lossless)) { throw 'Failed: lossless trim' }
$results += [ordered]@{ name='lossless-trim'; path=$lossless; bytes=(Get-Item $lossless).Length; passed=$true }

$precise = Join-Path $OutputDirectory 'precise-trim.mp4'
& $ffmpeg -hide_banner -loglevel error -y -ss 0.5 -i $source -t 1.5 -c:v libx264 -c:a aac $precise
if ($LASTEXITCODE -ne 0 -or !(Test-Path $precise)) { throw 'Failed: precise trim' }
$results += [ordered]@{ name='precise-trim'; path=$precise; bytes=(Get-Item $precise).Length; passed=$true }

$noAudio = Join-Path $OutputDirectory 'no-audio.mp4'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'color=c=blue:size=320x180:rate=24' -t 1 -an -c:v libx264 -pix_fmt yuv420p $noAudio
if ($LASTEXITCODE -ne 0 -or !(Test-Path $noAudio)) { throw 'Failed: no-audio sample' }
$results += [ordered]@{ name='no-audio-input'; path=$noAudio; bytes=(Get-Item $noAudio).Length; passed=$true }

$corrupt = Join-Path $OutputDirectory 'corrupt.mp4'
[IO.File]::WriteAllBytes($corrupt, [byte[]](0x00,0x11,0x22,0x33))
$savedErrorPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $ffprobe -v error $corrupt 2>$null
$corruptExitCode = $LASTEXITCODE
$ErrorActionPreference = $savedErrorPreference
$results += [ordered]@{ name='corrupt-rejected'; path=$corrupt; passed=($corruptExitCode -ne 0) }

$report = [ordered]@{
  generatedAt=(Get-Date).ToString('o')
  ffmpeg=(& $ffmpeg -version | Select-Object -First 1)
  engineSha256=(Get-FileHash $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
  outputDirectory=$OutputDirectory
  passed=($results.Where({ -not $_.passed }).Count -eq 0)
  results=$results
}
$reportPath = Join-Path $OutputDirectory 'media-matrix-report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content $reportPath -Encoding utf8
if (!$report.passed) { throw 'One or more media matrix checks failed.' }
Write-Output $reportPath
