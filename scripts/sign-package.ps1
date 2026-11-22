# Run only after obtaining an approved public-trust code-signing identity.
# The private key stays with its Windows certificate-store hardware/cloud provider.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [string]$SignToolPath,
    [ValidatePattern('^https?://')][string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Sign the Windows package on Windows using PowerShell 7.' }
. "$PSScriptRoot/package-state.ps1"
$directoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $directoryPath 'BUILD-INFO.json'))) { throw 'Use a completed JL Voice development package.' }
$certificatePath = "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not (Test-Path -LiteralPath $certificatePath)) { throw 'The signing certificate is not available in CurrentUser/My. No package files were changed.' }
$certificate = Get-Item -LiteralPath $certificatePath
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le [DateTime]::Now -or $certificate.NotBefore -gt [DateTime]::Now) { throw 'A current code-signing certificate with an available private-key provider is required.' }
if ($certificate.Subject -eq $certificate.Issuer) { throw 'Self-signed publisher certificates are not a public-trust fix for Smart App Control.' }
$codeSigningUsage = $false
foreach ($extension in $certificate.Extensions) {
    if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
        foreach ($usage in $extension.EnhancedKeyUsages) { if ($usage.Value -eq '1.3.6.1.5.5.7.3.3') { $codeSigningUsage = $true } }
    }
}
if (-not $codeSigningUsage) { throw 'The selected certificate is not a code-signing certificate.' }
$chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
try { if (-not $chain.Build($certificate)) { throw 'The signing identity does not build a trusted chain on this Windows host.' } }
finally { $chain.Dispose() }
if (-not $SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $versions = @(Get-ChildItem -LiteralPath $sdk -Directory | Where-Object Name -match '^\d+\.\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending)
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName 'x64\signtool.exe'
            if (Test-Path -LiteralPath $candidate) { $SignToolPath = $candidate; break }
        }
    }
}
if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Windows SDK SignTool is required on the signing machine.' }
& "$PSScriptRoot/check-package.ps1" -PackageDirectory $directoryPath -RejectUnlistedFiles
$inventory = @(Get-PackageCodeInventory $directoryPath)
$badExisting = @($inventory | Where-Object { $_.status -notin 'Valid','NotSigned' })
if ($badExisting.Count -gt 0) { throw 'Some existing code signatures are invalid. Investigate the package; they will not be overwritten.' }
$pending = @($inventory | Where-Object status -eq 'NotSigned')
Write-Output "Signing $($pending.Count) unsigned code file(s); existing valid vendor signatures are preserved."
foreach ($file in $pending) {
    & $SignToolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 (Join-Path $directoryPath $file.path)
    if ($LASTEXITCODE -ne 0) { throw "Signing/timestamping failed: $($file.path). Do not distribute this working folder." }
}
$verified = @(Get-PackageCodeInventory $directoryPath)
Assert-PackageSignatures $verified
foreach ($result in $verified | Where-Object signer_thumbprint -eq $CertificateThumbprint) {
    if (-not $result.timestamped) { throw "Publisher signature has no verified timestamp: $($result.path)" }
}
& "$PSScriptRoot/finalize-package.ps1" -PackageDirectory $directoryPath
Write-Output 'Signatures verified and hashes regenerated. This does not claim Smart App Control or release acceptance.'
