param([string]$ProjectPath = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath (Join-Path $ProjectPath 'hand-tracking\static\theatre\Build\theatre.loader.js'))) {
    throw 'The integrated WebGL stage is missing. Please install IntegratedTheatre first.'
}
if (!(Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop is required.' }
docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    $theatreDocker = @(
        "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe",
        "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$theatreDocker) { throw 'Start Docker Desktop, then try again.' }
    Start-Process -FilePath $theatreDocker -WindowStyle Hidden
    $theatreDeadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Seconds 2
        docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
        $theatreDockerReady = $LASTEXITCODE -eq 0
    } until ($theatreDockerReady -or (Get-Date) -gt $theatreDeadline)
    if (!$theatreDockerReady) { throw 'Docker Desktop is still starting. Try again shortly.' }
}
Push-Location -LiteralPath $ProjectPath
try {
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the hand-tracking service.' }
} finally { Pop-Location }
$theatreDeadline = (Get-Date).AddSeconds(45)
do {
    try {
        $theatreHealth = Invoke-RestMethod -Uri 'http://127.0.0.1:5000/health' -TimeoutSec 2
        if ($theatreHealth.status -eq 'ok') { Start-Process 'http://127.0.0.1:5000/'; exit 0 }
    } catch { }
    Start-Sleep -Seconds 1
} until ((Get-Date) -gt $theatreDeadline)
throw 'The service is still starting. Open http://127.0.0.1:5000/ shortly.'
