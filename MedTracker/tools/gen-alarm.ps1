#Requires -Version 5
# Generates wwwroot/audio/alarm.wav: a 1.0-second loopable two-beep pattern.
# 44100 Hz, mono, 16-bit PCM. Frequency and durations chosen so the sine begins
# and ends at zero-crossing (no click on loop boundary).

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path (Split-Path -Parent $scriptDir) 'wwwroot\audio\alarm.wav'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null

$sampleRate = 44100
$freq = 880.0           # A5
$amp = 0.35             # headroom
# Pattern (total = 1.0s so it loops cleanly):
#   0.20s tone (176 cycles @ 880Hz — integer, ends at zero)
#   0.30s silence
#   0.20s tone
#   0.30s silence
$segments = @(
    @{ Dur = 0.20; Tone = $true  }
    @{ Dur = 0.30; Tone = $false }
    @{ Dur = 0.20; Tone = $true  }
    @{ Dur = 0.30; Tone = $false }
)

$samples = New-Object System.Collections.Generic.List[int16]
$phase = 0.0
$phaseInc = 2.0 * [math]::PI * $freq / $sampleRate
foreach ($seg in $segments) {
    $n = [int]([math]::Round($seg.Dur * $sampleRate))
    for ($i = 0; $i -lt $n; $i++) {
        if ($seg.Tone) {
            $v = [math]::Sin($phase) * $amp
            $phase += $phaseInc
            $samples.Add([int16]([math]::Round($v * 32767)))
        } else {
            $samples.Add([int16]0)
        }
    }
    if (-not $seg.Tone) { $phase = 0.0 }
}

$dataBytes = New-Object byte[] ($samples.Count * 2)
for ($i = 0; $i -lt $samples.Count; $i++) {
    [BitConverter]::GetBytes($samples[$i]).CopyTo($dataBytes, $i * 2)
}

# WAV header (RIFF, PCM).
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
$bw.Write([uint32](36 + $dataBytes.Length))
$bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
$bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
$bw.Write([uint32]16)         # fmt chunk size
$bw.Write([uint16]1)          # PCM
$bw.Write([uint16]1)          # mono
$bw.Write([uint32]$sampleRate)
$bw.Write([uint32]($sampleRate * 2))  # byte rate
$bw.Write([uint16]2)          # block align
$bw.Write([uint16]16)         # bits per sample
$bw.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
$bw.Write([uint32]$dataBytes.Length)
$bw.Write($dataBytes)
$bw.Flush()
$bw.Close()

Write-Host "Wrote $out ($([math]::Round((Get-Item $out).Length / 1024, 1)) KB)"
