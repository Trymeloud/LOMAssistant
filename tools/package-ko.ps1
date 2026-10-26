param(
    [string]$Game = 'C:/Program Files (x86)/Steam/steamapps/common/LegendOfMortal',
    [string]$Distribution = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assembly = Get-Content -LiteralPath "$root/Properties/AssemblyInfo.cs" -Raw -Encoding UTF8
if ($assembly -notmatch 'AssemblyInformationalVersion\("([0-9]+\.[0-9]+\.[0-9]+-ko\.[0-9]+)"\)') {
    throw 'Missing Korean release version.'
}
$version = $Matches[1]
$status = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git status.' }
if ($status) { throw 'Commit the release sources before packaging so the manifest identifies the exact source.' }
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source commit.' }

$table = Join-Path $root 'localization/event-names-ko.tsv'
$ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadAllLines($table, [Text.Encoding]::UTF8)) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
    $fields = $line.Split([char]9)
    if ($fields.Length -ne 3 -or [string]::IsNullOrWhiteSpace($fields[0]) -or [string]::IsNullOrWhiteSpace($fields[2])) {
        throw 'Event translation table has an invalid row.'
    }
    if (-not $ids.Add($fields[0])) { throw "Duplicate event ID: $($fields[0])" }
    if ($fields[2] -match '[\u3400-\u4DBF\u4E00-\u9FFF]') { throw "Untranslated event label: $($fields[0])" }
}
if ($ids.Count -eq 0) { throw 'Event translation table is empty.' }

& "$root/build-ko.ps1" -Game $Game -Distribution $Distribution
$output = Join-Path $root "bin/release/$version"
$stage = Join-Path $output ('stage-' + [Guid]::NewGuid().ToString('N'))
$plugin = Join-Path $stage 'BepInEx/plugins/LOMAssistant'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
Copy-Item -LiteralPath "$root/bin/LOMAssistant.dll" -Destination $plugin
Copy-Item -LiteralPath $table -Destination $plugin
Copy-Item -LiteralPath "$root/releases/v$version.md" -Destination "$stage/INSTALL.ko.md"
$files = @('BepInEx/plugins/LOMAssistant/LOMAssistant.dll', 'BepInEx/plugins/LOMAssistant/event-names-ko.tsv', 'INSTALL.ko.md')
$hashes = [ordered]@{}
foreach ($file in $files) { $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $stage $file) -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = [ordered]@{
    version = $version
    sourceRepository = 'https://github.com/Trymeloud/LOMAssistant'
    sourceCommit = $commit.Trim()
    upstreamVersion = '2.9.3'
    packageType = 'patch; requires original LOMAssistant 2.9.3 with BepInEx 6'
    builtUtc = [DateTime]::UtcNow.ToString('o')
    eventTranslations = $ids.Count
    sha256 = $hashes
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$stage/release-info.json" -Encoding UTF8
$zip = Join-Path $output "LOMAssistant-$version-patch.zip"
Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$output/SHA256SUMS.txt" -Encoding ASCII
Write-Output "Packaged $zip ($($ids.Count) event translations; source $commit)."
