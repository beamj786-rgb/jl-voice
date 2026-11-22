param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Finalize the Windows package on Windows using PowerShell 7.' }
. "$PSScriptRoot/package-state.ps1"
$directoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$infoPath = Join-Path $directoryPath 'BUILD-INFO.json'
$info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json -AsHashtable
$inventory = @(Get-PackageCodeInventory $directoryPath)
$allValid = @($inventory | Where-Object status -ne 'Valid').Count -eq 0
$inventory | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $directoryPath 'SIGNATURES.json') -Encoding utf8NoBOM
$info.worker_signature_status = ($inventory | Where-Object path -eq 'runtime\JLVoiceAsr.exe').status
$info.all_code_signatures_valid = $allValid
$info.signing_status = if ($allValid) { 'authenticode-verified-on-build-host' } else { 'incomplete-signatures-development' }
# Authenticode verification is necessary evidence, not an enforced Smart App Control test.
$info.smart_app_control_qualified = $false
$info | ConvertTo-Json -Depth 5 | Set-Content $infoPath -Encoding utf8NoBOM
$manifestPath = Join-Path $directoryPath 'SHA256SUMS.json'
Get-ChildItem -LiteralPath $directoryPath -Recurse -File | Where-Object FullName -ne $manifestPath | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($directoryPath.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
} | ConvertTo-Json | Set-Content $manifestPath -Encoding utf8NoBOM
Write-Output "Finalized package: $($inventory.Count) code files; all signatures valid: $allValid. Smart App Control acceptance remains separate."
