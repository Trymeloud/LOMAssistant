param([string]$GameFlags, [string]$LiveConditions, [string]$ModAssembly)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output = Join-Path $root 'bin/ko-check'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'A .NET SDK is required.' }
$compiler = Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore/csc.dll"
$dotnetRoot = Split-Path $Matches[2] -Parent
$ref = Get-ChildItem "$dotnetRoot/packs/Microsoft.NETCore.App.Ref" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$framework = Get-ChildItem "$($ref.FullName)/ref" -Directory | Select-Object -First 1
$arguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:12', "-out:$output/ko-check.dll")
Get-ChildItem $framework.FullName -Filter '*.dll' | ForEach-Object { $arguments += "-r:$($_.FullName)" }
foreach ($file in @('UiText', 'UiTextEnglish', 'UiQuestNames', 'UiConditionNames', 'UiFlagNames', 'UiTextKorean', 'UiTextKorean.Data', 'UiTextKorean.Notes')) { $arguments += "$root/LOMAssistant/$file.cs" }
$arguments += "$root/LOMAssistant/KoreanText.cs"
$arguments += "$root/LOMAssistant/KoreanStoryText.cs"
$arguments += "$root/LOMAssistant/KoreanLabels.cs"
$arguments += "$root/LOMAssistant/KoreanActionCall.cs"
$arguments += "$PSScriptRoot/NativeChecks.cs"
$arguments += "$PSScriptRoot/StoryChecks.cs"
$arguments += "$PSScriptRoot/LayoutChecks.cs"
$arguments += "$PSScriptRoot/BuiltModChecks.cs"
$cecil = Join-Path (Split-Path $root -Parent) 'BepInEx/core/Mono.Cecil.dll'
$arguments += "-r:$cecil"
Copy-Item -LiteralPath $cecil -Destination $output -Force
$arguments += "-resource:$root/localization/flag-names-ko.tsv,LOMAssistant.flag-names-ko.tsv"
$arguments += "$PSScriptRoot/Program.cs"
$arguments += "$PSScriptRoot/Audit.cs"
$arguments += "$PSScriptRoot/FlagAudit.cs"
$arguments += "$root/LOMAssistant.Features.Ai/AiReplyLanguage.cs"
$arguments += "$root/LOMAssistant.Features.Ai/AiKoreanVoice.cs"
$arguments += "$root/LOMAssistant/EventNameTable.cs"
foreach ($assembly in @('Microsoft.CodeAnalysis', 'Microsoft.CodeAnalysis.CSharp')) {
    $file = Join-Path (Split-Path $compiler -Parent) "$assembly.dll"
    $arguments += "-r:$file"
    Copy-Item -LiteralPath $file -Destination $output -Force
}
& dotnet $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed.' }
$runtime = @{runtimeOptions = @{tfm = $framework.Name; framework = @{name='Microsoft.NETCore.App'; version=$ref.Name}; rollForward='LatestMinor'}}
$runtime | ConvertTo-Json -Depth 5 | Set-Content "$output/ko-check.runtimeconfig.json" -Encoding UTF8
$checkArgs = @("$output/ko-check.dll", "$root/LOMAssistant/conditions.json.gz", $root)
if ($GameFlags) { $checkArgs += @($GameFlags, $LiveConditions) }
if ($ModAssembly) {
    # Windows PowerShell drops empty native-command arguments. Preserve positions.
    if (-not $GameFlags) { $checkArgs += @('--no-game-flags', '--no-live-conditions') }
    $checkArgs += $ModAssembly
}
& dotnet @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'Translation checks failed.' }
