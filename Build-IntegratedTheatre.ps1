param([string]$UnityPath)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'unity\UnityPuppetTest'
$log = Join-Path $root 'unity-integrated-build.log'
if (-not $UnityPath) {
    $version = (Get-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') |
        Select-String '^m_EditorVersion: (.+)$').Matches.Groups[1].Value
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity was not found at $UnityPath. Install the project version or pass -UnityPath."
}
$arguments = @('-batchmode', '-quit', '-nographics', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'IntegratedTheatreBuilder.Build', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
$contents = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
if ($process.ExitCode -ne 0 -or $contents -notmatch 'INTEGRATED_THEATRE_BUILD_PASS') {
    if ($contents -match 'No valid Unity Editor license') {
        throw "Unity needs an active Editor license in Unity Hub. See $log"
    }
    throw "The integrated WebGL build did not pass (exit $($process.ExitCode)). See $log"
}
Write-Output "Integrated WebGL build passed. See $log"
