$ErrorActionPreference = "Stop"

$project = Split-Path -Parent $PSScriptRoot
$serverScript = Join-Path $PSScriptRoot "mock_puppet_server.py"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.3f1\Editor\Unity.exe"
$report = Join-Path $project "PuppetChannelIntegrationReport.json"
$log = Join-Path $project "PuppetChannelIntegration.log"

if (-not (Test-Path -LiteralPath $unity)) {
    throw "Unity 6000.5.3f1 was not found at $unity"
}

$server = Start-Process `
    -FilePath "python" `
    -ArgumentList @("`"$serverScript`"") `
    -WindowStyle Hidden `
    -PassThru

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:5000/health" -TimeoutSec 2
            if ($health.status -eq "ok") {
                $ready = $true
                break
            }
        }
        catch {
        }
    }

    if (-not $ready) {
        throw "The mock puppet server did not start."
    }

    $reportTimeBeforeRun = if (Test-Path -LiteralPath $report) {
        (Get-Item -LiteralPath $report).LastWriteTimeUtc
    }
    else {
        [DateTime]::MinValue
    }

    $arguments = @(
        "-projectPath", "`"$project`"",
        "-executeMethod", "PuppetChannelIntegrationLauncher.Run",
        "-puppetChannelIntegrationTest",
        "-logFile", "`"$log`""
    )
    $editor = Start-Process `
        -FilePath $unity `
        -ArgumentList $arguments `
        -WindowStyle Hidden `
        -PassThru

    if (-not $editor.WaitForExit(600000)) {
        Stop-Process -Id $editor.Id -Force
        throw "Unity did not finish within ten minutes. See $log"
    }

    if (-not (Test-Path -LiteralPath $report)) {
        throw "Unity exited without creating the integration report. See $log"
    }
    if ((Get-Item -LiteralPath $report).LastWriteTimeUtc -le $reportTimeBeforeRun) {
        throw "Unity did not update the integration report. See $log"
    }

    Get-Content -Raw -LiteralPath $report
}
finally {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
    }
}
