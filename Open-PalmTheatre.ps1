param([switch]$NoBrowser, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$compose = Join-Path $PSScriptRoot 'docker-compose.yml'
if (!(Test-Path -LiteralPath $compose)) { throw 'The downloaded integrated theatre configuration is missing.' }
$docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (!$docker) {
    $docker = @(
        "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin\docker.exe",
        "$env:ProgramFiles\Docker\Docker\resources\bin\docker.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$docker) { throw 'Docker Desktop is required.' }

function Test-DockerReady {
    try {
        & $docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    } catch { return $false }
}
if (!(Test-DockerReady)) {
    $desktop = @(
        "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe",
        "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$desktop) { throw 'Start Docker Desktop, then try again.' }
    Write-Output 'Starting Docker Desktop...'
    Start-Process -FilePath $desktop -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(120)
    do {
        Start-Sleep -Seconds 2
        $dockerReady = Test-DockerReady
    } until ($dockerReady -or (Get-Date) -gt $deadline)
    if (!$dockerReady) { throw 'Docker Desktop is still starting. Run this launcher again shortly.' }
}

try {
    & $docker image inspect 'palm-theatre-integration:20261106' 2>$null | Out-Null
    $imageReady = $LASTEXITCODE -eq 0
} catch { $imageReady = $false }
if ($imageReady -and !$Rebuild) {
    & $docker compose -f $compose up -d --no-build
} else {
    & $docker compose -f $compose up -d --build
}
if ($LASTEXITCODE -ne 0) { throw 'Could not start the integrated theatre. See the Docker message above.' }

$deadline = (Get-Date).AddSeconds(90)
do {
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5000/health' -TimeoutSec 3
        if ($health.status -eq 'ok') {
            Write-Output 'Integrated theatre ready: http://127.0.0.1:5000/'
            if (!$NoBrowser) { Start-Process 'http://127.0.0.1:5000/' }
            exit 0
        }
    } catch { }
    Start-Sleep -Seconds 1
} until ((Get-Date) -gt $deadline)
throw 'The preview did not become ready. Check: docker logs unity-puppet-hand-tracking'
