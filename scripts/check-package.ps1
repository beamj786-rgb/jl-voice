# Compatible with Windows PowerShell 5.1; no developer runtime or administrator rights needed.
param([string]$PackageDirectory = $PSScriptRoot, [switch]$RejectUnlistedFiles)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$manifest = Join-Path $packageDirectory 'SHA256SUMS.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Missing SHA256SUMS.json. Extract the entire test artifact first.' }
$entries = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($null -eq $entries -or @($entries).Count -eq 0) { throw 'The package hash manifest is empty.' }
$failures = New-Object 'System.Collections.Generic.List[string]'
$listed = @{}
$prefix = $packageDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($entry in $entries) {
    $file = [IO.Path]::GetFullPath((Join-Path $packageDirectory $entry.path))
    if ($listed.ContainsKey($file)) { throw 'Duplicate path in package manifest.' }
    $listed[$file] = $true
    if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid path in package manifest.' }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $failures.Add("Missing: $($entry.path)"); continue }
    # Portable settings are intentionally user-editable after packaging.
    if ($entry.path -eq 'settings.json') { continue }
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) { $failures.Add("Hash mismatch: $($entry.path)") }
}
if ($RejectUnlistedFiles) {
    foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -Recurse -File) {
        if ($file.FullName -ne $manifest -and -not $listed.ContainsKey($file.FullName)) { $failures.Add("Unlisted: $($file.Name)") }
    }
}
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host $failure }
    throw 'Package check failed. Extract a fresh, complete artifact into a new folder.'
}
Write-Output "PASS: static package files match SHA256SUMS.json ($(@($entries).Count) manifest entries; editable settings excluded)."
Write-Output 'This verifies file integrity, not publisher authenticity, recognition quality or minimum hardware support.'
