# Developer helpers. Windows PowerShell/PowerShell can inspect; finalization uses PowerShell 7.
function Get-PackageCodeInventory([string]$Directory) {
    $directoryPath = (Resolve-Path -LiteralPath $Directory).Path
    $files = @(Get-ChildItem -LiteralPath $directoryPath -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' } | Sort-Object FullName)
    if ($files.Count -eq 0) { throw 'The package contains no executable code.' }
    foreach ($file in $files) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
        [pscustomobject]@{
            path = $file.FullName.Substring($directoryPath.Length + 1)
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            status = $signature.Status.ToString()
            signer_thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
            timestamped = $null -ne $signature.TimeStamperCertificate
            product_name = $versionInfo.ProductName
            product_version = $versionInfo.ProductVersion
            file_version = $versionInfo.FileVersion
        }
    }
}

function Assert-PackageSignatures([object[]]$Inventory) {
    if ($Inventory.Count -eq 0) { throw 'Cannot verify an empty signature inventory.' }
    $invalid = @($Inventory | Where-Object status -ne 'Valid')
    if ($invalid.Count -gt 0) {
        $names = ($invalid | Select-Object -First 8 | ForEach-Object { "$($_.path): $($_.status)" }) -join '; '
        throw "Code signature verification failed for $($invalid.Count) file(s). $names"
    }
}
