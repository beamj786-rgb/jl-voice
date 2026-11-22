param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoDirectory = Split-Path $PSScriptRoot -Parent
. "$repoDirectory/scripts/package-state.ps1"
$directoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$inventory = @(Get-Content (Join-Path $directoryPath 'SIGNATURES.json') -Raw | ConvertFrom-Json)
$info = Get-Content (Join-Path $directoryPath 'BUILD-INFO.json') -Raw | ConvertFrom-Json
if ($info.smart_app_control_qualified -or $info.all_code_signatures_valid) { throw 'Unsigned development build was incorrectly labelled qualified.' }
if (@($inventory | Where-Object { $_.path -eq 'runtime\JLVoiceAsr.exe' -and $_.status -eq 'NotSigned' }).Count -ne 1) { throw 'Expected the actual development worker to be reported unsigned.' }
$rejected = $false
try { Assert-PackageSignatures $inventory } catch { if ($_.Exception.Message -like 'Code signature verification failed*') { $rejected = $true } else { throw } }
if (-not $rejected) { throw 'Unsigned package passed the signature gate.' }
Write-Output 'PASS: actual unsigned worker is inventoried and fails the signed-package gate.'
$rejected = $false
try { & "$repoDirectory/scripts/sign-package.ps1" -PackageDirectory $directoryPath -CertificateThumbprint ('0' * 40) }
catch { if ($_.Exception.Message -like 'The signing certificate is not available*') { $rejected = $true } else { throw } }
if (-not $rejected) { throw 'Missing certificate did not stop signing.' }
Write-Output 'PASS: missing signing identity is rejected before package mutation.'
# A test-only leaf in CurrentUser/My. Never trusted in Root, never used to sign code.
$fixture = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=JL Voice CI rejection fixture' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter ([DateTime]::Now.AddDays(1)) -KeyExportPolicy NonExportable -HashAlgorithm SHA256
try {
    $rejected = $false
    try { & "$repoDirectory/scripts/sign-package.ps1" -PackageDirectory $directoryPath -CertificateThumbprint $fixture.Thumbprint }
    catch { if ($_.Exception.Message -like 'Self-signed publisher certificates*') { $rejected = $true } else { throw } }
    if (-not $rejected) { throw 'Self-signed identity was accepted as a public-trust fix.' }
}
finally { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($fixture.Thumbprint)" -DeleteKey }
& (Join-Path $directoryPath 'Check-Package.ps1')
Write-Output 'PASS: test-only self-signed identity is rejected; package files remain intact.'
& "$repoDirectory/scripts/finalize-package.ps1" -PackageDirectory $directoryPath
$manifest = @(Get-Content (Join-Path $directoryPath 'SHA256SUMS.json') -Raw | ConvertFrom-Json)
if (@($manifest | Where-Object path -eq 'SHA256SUMS.json').Count -ne 0) { throw 'Manifest incorrectly includes itself.' }
& (Join-Path $directoryPath 'Check-Package.ps1')
Write-Output 'PASS: post-sign finalization regenerates consistent hashes without a self-entry.'
Write-Output 'No public-trust signing or enforced Smart App Control test was performed.'
