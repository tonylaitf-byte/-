[CmdletBinding()]
param([switch]$SkipModelDownload)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.voice-runtime'
$basePython = Join-Path $runtimeRoot 'python310\python.exe'
$qwenEnv = Join-Path $runtimeRoot 'qwen-env'
$qwenPython = Join-Path $qwenEnv 'Scripts\python.exe'
$modelRoot = Join-Path $runtimeRoot 'models\Qwen3-TTS-12Hz-0.6B-CustomVoice'
$env:HF_HOME = Join-Path $runtimeRoot 'hf-cache'
$env:PIP_CACHE_DIR = Join-Path $runtimeRoot 'pip-cache'
$env:PYTHONUTF8 = '1'

if (-not (Test-Path -LiteralPath $qwenPython)) {
    & $basePython -m venv --system-site-packages $qwenEnv
}

& $qwenPython -m pip install --upgrade pip wheel
& $qwenPython -m pip install --upgrade qwen-tts huggingface_hub hf_xet

if (-not $SkipModelDownload) {
    $downloadScript = @"
from huggingface_hub import snapshot_download
snapshot_download(
    repo_id='Qwen/Qwen3-TTS-12Hz-0.6B-CustomVoice',
    local_dir=r'$modelRoot',
    max_workers=2,
)
"@
    $downloadScript | & $qwenPython -
}

Write-Host '[Ready] Qwen3-TTS CustomVoice is installed.'
Write-Host "Run: $qwenPython $projectRoot\generate_taiwanese_voice.py --all --candidates --backend qwen3-tts --overwrite"
