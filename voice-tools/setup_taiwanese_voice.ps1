[CmdletBinding()]
param(
    [switch]$SkipModelDownload
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.voice-runtime'
$pythonRoot = Join-Path $runtimeRoot 'python310'
$pythonExe = Join-Path $pythonRoot 'python.exe'
$cosyRepo = Join-Path $runtimeRoot 'CosyVoice'
$modelRoot = Join-Path $runtimeRoot 'models\Fun-CosyVoice3-0.5B-2512'
$pipCache = Join-Path $runtimeRoot 'pip-cache'
$promptDir = Join-Path $PSScriptRoot 'prompts'
$promptWav = Join-Path $promptDir 'taiwanese_male_prompt.wav'
$deepPromptWav = Join-Path $promptDir 'mature_male_deep_prompt.wav'

New-Item -ItemType Directory -Force -Path $runtimeRoot, $pipCache, $promptDir | Out-Null
$env:PIP_CACHE_DIR = $pipCache
$env:PYTHONUTF8 = '1'
$env:HF_HOME = Join-Path $runtimeRoot 'hf-cache'
$env:HF_HUB_DOWNLOAD_TIMEOUT = '300'
$env:HF_HUB_ETAG_TIMEOUT = '60'
$env:MODELSCOPE_CACHE = Join-Path $runtimeRoot 'modelscope-cache'

if (-not (Test-Path -LiteralPath $pythonExe)) {
    throw "找不到專案內 Python 3.10：$pythonExe。請先安裝 Python 3.10.11 到此目錄。"
}

if (-not (Test-Path -LiteralPath (Join-Path $cosyRepo 'cosyvoice'))) {
    Write-Host '[設定] 下載 CosyVoice3 原始碼...'
    git clone --recursive https://github.com/FunAudioLLM/CosyVoice.git $cosyRepo
}

Write-Host '[設定] 安裝 PyTorch CUDA 12.1...'
& $pythonExe -m pip install --upgrade pip wheel
& $pythonExe -m pip install setuptools==70.3.0
& $pythonExe -m pip install torch==2.3.1 torchaudio==2.3.1 --index-url https://download.pytorch.org/whl/cu121

Write-Host '[設定] 安裝 CosyVoice3 推論套件...'
& $pythonExe -m pip install openai-whisper==20231117 --no-build-isolation
& $pythonExe -m pip install -r (Join-Path $cosyRepo 'requirements.txt')
# WeText downloads a Mandarin-oriented normalizer during model construction.
# Keep the original Han characters so CosyVoice3 can follow the Minnan prompt.
& $pythonExe -m pip uninstall -y wetext

if (-not $SkipModelDownload) {
    Write-Host '[設定] 下載 Fun-CosyVoice3-0.5B-2512 模型...'
    & $pythonExe -m pip install --upgrade huggingface_hub hf_xet
    $downloadScript = @"
from huggingface_hub import snapshot_download
snapshot_download(
    repo_id='FunAudioLLM/Fun-CosyVoice3-0.5B-2512',
    local_dir=r'$modelRoot',
    ignore_patterns=['llm.rl.pt', 'speech_tokenizer_v3.batch.onnx'],
    max_workers=2,
)
"@
    $downloadScript | & $pythonExe -
}

if (-not (Test-Path -LiteralPath $promptWav)) {
    Write-Host '[設定] 建立男性角色音色參考 WAV（Microsoft Zhiwei zh-TW）...'
    Add-Type -AssemblyName System.Speech
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice('Microsoft Zhiwei')
        $synth.Rate = -1
        $synth.Volume = 92
        $synth.SetOutputToWaveFile($promptWav)
        $synth.Speak('在下行走江湖多年，今日有緣相逢，請多指教。')
    }
    finally {
        $synth.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $deepPromptWav)) {
    Write-Host '[設定] 建立成熟中低音男性角色參考 WAV（只塑形參考音色）...'
    $promptScript = @"
import librosa
import soundfile as sf
x, sr = sf.read(r'$promptWav', dtype='float32')
if x.ndim > 1:
    x = x.mean(axis=1)
y = librosa.effects.pitch_shift(x, sr=sr, n_steps=-2.0, bins_per_octave=12)
sf.write(r'$deepPromptWav', y, sr, subtype='PCM_16')
"@
    $promptScript | & $pythonExe -
}

Write-Host '[完成] 台語角色語音環境已就緒。'
Write-Host "執行：$pythonExe $projectRoot\generate_taiwanese_voice.py --all --candidates"
