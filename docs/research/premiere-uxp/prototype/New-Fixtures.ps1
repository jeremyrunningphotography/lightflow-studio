param([string]$FfmpegPath = '', [string]$FfprobePath = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
if (-not $FfmpegPath) { $FfmpegPath = Join-Path $repo 'artifacts/release/LightflowStudio/ffmpeg/bin/ffmpeg.exe' }
if (-not $FfprobePath) { $FfprobePath = Join-Path $repo 'artifacts/release/LightflowStudio/ffmpeg/bin/ffprobe.exe' }
$directory = Join-Path $repo ('artifacts/research/premiere-256/' + [Guid]::NewGuid().ToString('D'))
$null = New-Item -ItemType Directory -Path $directory
# New random directory and -n: never overwrite existing media.
& $FfmpegPath -hide_banner -loglevel error -n -f lavfi -i 'testsrc2=size=640x360:rate=30:duration=6' -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=6' -map 0:v -map 1:a -c:v mpeg4 -q:v 3 -pix_fmt yuv420p -c:a pcm_s16le -ac 2 -timecode '01:00:00:00' (Join-Path $directory 'source.mov')
if ($LASTEXITCODE -ne 0) { throw 'Source fixture encode failed' }
& $FfmpegPath -hide_banner -loglevel error -n -i (Join-Path $directory 'source.mov') -map 0:v:0 -map 0:a:0 -vf 'scale=320:180' -c:v mpeg4 -q:v 5 -c:a pcm_s16le -ac 2 -timecode '01:00:00:00' (Join-Path $directory 'proxy.mov')
if ($LASTEXITCODE -ne 0) { throw 'Proxy fixture encode failed' }
$fixtures = foreach ($name in @('source.mov','proxy.mov')) {
  $path = Join-Path $directory $name
  $probe = & $FfprobePath -v error -show_streams -show_format -of json $path | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw "Probe failed: $name" }
  $video = $probe.streams | Where-Object codec_type -eq video
  $audio = $probe.streams | Where-Object codec_type -eq audio
  if ($video.r_frame_rate -ne '30/1' -or $video.nb_frames -ne '180' -or $audio.channels -ne 2) { throw "Fixture verification failed: $name" }
  @{name=$name; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; probe=$probe}
}
$fixture = @{schemaVersion=1; purpose='Lightflow issue 256 disposable generated media'; catalogId=[Guid]::NewGuid().ToString(); assetId=[Guid]::NewGuid().ToString(); subclipId=[Guid]::NewGuid().ToString(); generatedUtc=[DateTime]::UtcNow.ToString('o'); files=@($fixtures)}
[IO.File]::WriteAllText((Join-Path $directory 'fixture.json'), ($fixture | ConvertTo-Json -Depth 16))
Write-Output $directory
