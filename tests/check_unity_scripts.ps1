param([string]$EditorData = 'C:\Program Files\Unity\Hub\Editor\6000.5.3f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'unity\UnityPuppetTest\Assets'
$output = Join-Path $root 'unity\UnityPuppetTest\Library\HybridCompileCheck'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sdk = Get-ChildItem (Join-Path $EditorData 'DotNetSdk\sdk') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$dotnet = Join-Path $EditorData 'NetCoreRuntime\dotnet.exe'
$references = @(Get-ChildItem (Join-Path $EditorData 'UnityReferenceAssemblies\unity-4.8-api') -Filter '*.dll' -Recurse |
    ForEach-Object { '/reference:"' + $_.FullName + '"' })
$references += Get-ChildItem (Join-Path $EditorData 'Managed') -Filter '*.dll' -Recurse |
    ForEach-Object { '/reference:"' + $_.FullName + '"' }
$common = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/langversion:latest',
    '/define:UNITY_EDITOR,UNITY_6000_5_OR_NEWER,UNITY_STANDALONE_WIN')
$scripts = Get-ChildItem $assets -Filter '*.cs' -Recurse
$runtime = @($scripts | Where-Object { $_.FullName -notmatch '[\\/]Editor[\\/]' } | ForEach-Object { '"' + $_.FullName + '"' })
$editor = @($scripts | Where-Object { $_.FullName -match '[\\/]Editor[\\/]' } | ForEach-Object { '"' + $_.FullName + '"' })
$runtimeDll = Join-Path $output 'Assembly-CSharp.dll'
$runtimeResponse = Join-Path $output 'runtime.rsp'
[IO.File]::WriteAllLines($runtimeResponse, @($common) + @($references) + @('/out:"' + $runtimeDll + '"') + $runtime)
& $dotnet $compiler ('@' + $runtimeResponse)
if ($LASTEXITCODE -ne 0) { throw 'Unity runtime C# compilation failed.' }
$editorResponse = Join-Path $output 'editor.rsp'
[IO.File]::WriteAllLines($editorResponse, @($common) + @($references) + @('/reference:"' + $runtimeDll + '"',
    '/out:"' + (Join-Path $output 'Assembly-CSharp-Editor.dll') + '"') + $editor)
& $dotnet $compiler ('@' + $editorResponse)
if ($LASTEXITCODE -ne 0) { throw 'Unity editor C# compilation failed.' }
Write-Output 'Unity runtime and editor C# compilation passed (assembly-only check; not a Unity build).'
