param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$directoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$ownedPaths = @('JLVoice.exe','JLVoice.dll','runtime\JLVoiceAsr.exe')
$observed = @(foreach ($path in $ownedPaths) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directoryPath $path))
    if ($info.ProductName -ne 'JL Voice' -or $info.ProductVersion -ne '0.1.0' -or $info.FileVersion -ne '0.1.0.0') { throw "Owned binary metadata mismatch: $path ($($info.ProductName), $($info.ProductVersion), $($info.FileVersion))" }
    [pscustomobject]@{ path=$path; product=$info.ProductName; product_version=$info.ProductVersion; file_version=$info.FileVersion }
})
$report = [ordered]@{
    owned_metadata_checked = $true
    owned_binaries = $observed
    foundation_approved = $false
    submitted = $false
    original_code_license = 'MIT'
    license_scope = 'original code only; dependency/font/brand treatment remains separate'
    full_bundle_license_scope_approved = $false
    dependency_signing_scope_approved = $false
    smart_app_control_qualified = $false
    note = 'Only original project binaries are metadata-normalized. Third-party product/version metadata is preserved. This is not signing or Foundation eligibility approval.'
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM
Write-Output 'PASS: the three original-project Windows binaries have consistent product/version metadata.'
Write-Output 'Foundation admission, licensing, dependency signing and Smart App Control acceptance remain unresolved.'
