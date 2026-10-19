$ErrorActionPreference = "Stop"

$project = Split-Path -Parent $PSScriptRoot
$marker = Join-Path $project "RunPuppetLiveCamera.request"
$report = Join-Path $project "PuppetLiveCameraReport.json"
$unityLock = Join-Path $project "Temp\UnityLockfile"
$unityEditor = Get-Process Unity -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $unityEditor -or -not (Test-Path -LiteralPath $unityLock)) {
    throw "Open the UnityPuppetTest project in Unity before running this test."
}

function Test-BridgeHealth {
    try {
        $health = Invoke-RestMethod `
            -Uri "http://127.0.0.1:5000/health" `
            -TimeoutSec 2
        return $health.status -eq "ok" -and $health.cameraOpened
    }
    catch {
        return $false
    }
}

try {
    if (-not (Test-BridgeHealth)) {
        throw "Start Docker with Start-Puppet.bat, open http://127.0.0.1:5000/, and enable the browser camera first."
    }

    $reportTimeBeforeRun = if (Test-Path -LiteralPath $report) {
        (Get-Item -LiteralPath $report).LastWriteTimeUtc
    }
    else {
        [DateTime]::MinValue
    }

    [IO.File]::WriteAllText($marker, [DateTime]::UtcNow.ToString("O"))

    Write-Host ""
    Write-Host "Live puppet test is ready." -ForegroundColor Green
    Write-Host "1. Hold one open hand naturally in the camera for about two seconds."
    Write-Host "2. Move the thumb, pinky, middle finger, and then the whole palm."
    Write-Host "3. Unity will stop Play mode and print the result automatically."
    Write-Host ""

    $deadline = [DateTime]::UtcNow.AddSeconds(160)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if (
            (Test-Path -LiteralPath $report) -and
            (Get-Item -LiteralPath $report).LastWriteTimeUtc -gt $reportTimeBeforeRun
        ) {
            $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
            Get-Content -Raw -LiteralPath $report
            if (-not $result.success) {
                throw "The live camera test finished but did not pass."
            }
            exit 0
        }
    }

    throw "The live camera test did not finish within 160 seconds."
}
finally {
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
}
