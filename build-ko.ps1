param(
    [string]$Game = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal',
    [string]$Distribution = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $Game 'Mortal_Data/Managed'
$core = Join-Path $Distribution 'BepInEx/core'
$plugins = Join-Path $Distribution 'BepInEx/plugins/LOMAssistant'
$output = Join-Path $PSScriptRoot 'bin'
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'A .NET SDK is required.' }
$compiler = Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore/csc.dll"
if (-not (Test-Path -LiteralPath "$managed/Assembly-CSharp.dll")) { throw "Game assemblies not found: $managed" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$common = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-langversion:12', '-optimize+', '-deterministic')
$shim = $common + @("-out:$output/BepInEx.dll")
foreach ($name in @('mscorlib', 'netstandard', 'System', 'System.Core', 'UnityEngine.CoreModule')) { $shim += "-r:$managed/$name.dll" }
$shim += @("-r:$core/BepInEx.Core.dll", "$PSScriptRoot/tools/bepinex5-shim/BepInEx5Shim.cs")
& dotnet $compiler @shim
if ($LASTEXITCODE -ne 0) { throw 'Compatibility shim compilation failed.' }
$arguments = $common + @('-nowarn:CS1701,CS1702', "-out:$output/LOMAssistant.dll")
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { $arguments += "-r:$($_.FullName)" }
foreach ($name in @('BepInEx.Core', 'BepInEx.Unity.Mono', '0Harmony', 'Mono.Cecil')) { $arguments += "-r:$core/$name.dll" }
$arguments += "-r:$plugins/Newtonsoft.Json.dll"
$resources = @{
    'LOMAssistant.Features.AnytimeSave/Replay.lua' = 'LOMAssistant.AnytimeSave.Replay.lua'
    'LOMAssistant.Features.AnytimeSave/contract.tsv.gz' = 'LOMAssistant.AnytimeSave.contract.tsv.gz'
    'LOMAssistant/conditions.json.gz' = 'LOMAssistant.conditions.json.gz'
    'localization/flag-names-ko.tsv' = 'LOMAssistant.flag-names-ko.tsv'
    'CHANGELOG.md' = 'LOMAssistant.CHANGELOG.md'
    'CHANGELOG.ja.md' = 'LOMAssistant.CHANGELOG.ja.md'
    'CHANGELOG.ko.md' = 'LOMAssistant.CHANGELOG.ko.md'
    'bin/BepInEx.dll' = 'LOMAssistant.BepInEx5Shim.dll'
}
foreach ($entry in $resources.GetEnumerator()) { $arguments += "-resource:$PSScriptRoot/$($entry.Key),$($entry.Value)" }
Get-ChildItem -LiteralPath $PSScriptRoot -Directory | Where-Object { $_.Name -like 'LOMAssistant*' -or $_.Name -eq 'Properties' } | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Filter '*.cs' -Recurse | ForEach-Object { $arguments += $_.FullName }
}
& dotnet $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed.' }
Write-Output "Built $output/LOMAssistant.dll (game installation was not modified)."
