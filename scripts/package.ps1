param([ValidateSet('ggml-base-q5_1.bin','ggml-base-q8_0.bin','ggml-small-q5_1.bin')][string]$Model = 'ggml-base-q5_1.bin', [switch]$IncludeSmall, [switch]$IncludeTurbo)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Build the native portable package on Windows with PowerShell 7.' }
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path "$root/artifacts/JLVoice") { throw 'Move the previous artifacts/JLVoice folder aside before packaging; do not mix old and new runtimes/models.' }
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Build command failed: $LASTEXITCODE" } }
foreach ($tool in @('git','cmake','dotnet')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Developer tool missing: $tool" } }
$vendor = Join-Path $root 'vendor/whisper.cpp'
$revision = 'd1be6fde11ac6e0407606b4e42fe72d34add8037' # upstream v1.9.5
if (-not (Test-Path "$vendor/.git")) { git clone https://github.com/ggml-org/whisper.cpp.git $vendor; Check-Exit }
# Do not reset a modified vendor checkout.
if (git -C $vendor status --porcelain) { throw 'Vendor checkout has local changes; preserve them before building.' }
git -C $vendor checkout --detach $revision; Check-Exit
$build = Join-Path $root 'artifacts/native'
# Runtime CPU dispatch avoids compiling for the build machine. No GPU or OpenMP.
cmake -S "$root/native" -B $build -A x64 "-DWHISPER_SOURCE=$vendor" -DGGML_BACKEND_DL=ON -DGGML_CPU_ALL_VARIANTS=ON -DBUILD_SHARED_LIBS=ON -DCMAKE_POLICY_DEFAULT_CMP0091=NEW '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded$<$<CONFIG:Debug>:Debug>'
Check-Exit
cmake --build $build --config Release --target JLVoiceAsr --parallel 2; Check-Exit
$output = Join-Path $root 'artifacts/JLVoice'
New-Item -ItemType Directory -Force $output,"$output/runtime","$output/models","$output/licenses" | Out-Null
dotnet publish "$root/src/JLVoice/JLVoice.csproj" -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=false -o $output
Check-Exit
Get-ChildItem "$build/bin/Release" -File | Where-Object { $_.Extension -in '.dll','.exe' } | Copy-Item -Destination "$output/runtime"
$manifest = Get-Content "$root/models/manifest.json" -Raw | ConvertFrom-Json
$includedModels = @($Model)
if ($IncludeSmall -and $Model -ne 'ggml-small-q5_1.bin') { $includedModels += 'ggml-small-q5_1.bin' }
if ($IncludeTurbo) { $includedModels += 'ggml-large-v3-turbo-q5_0.bin' } # Experimental Czech test candidate, not default.
foreach ($includedModel in $includedModels) {
    $entry = $manifest.models | Where-Object file -eq $includedModel
    $cached = Join-Path $root "models/$includedModel"
    if (-not (Test-Path $cached)) {
        Invoke-WebRequest "https://huggingface.co/$($manifest.repository)/resolve/$($manifest.revision)/$includedModel" -OutFile "$cached.part"
        if ((Get-FileHash "$cached.part" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256 -or (Get-Item "$cached.part").Length -ne $entry.bytes) { throw 'Model download integrity mismatch.' }
        Move-Item "$cached.part" $cached
    }
    if ((Get-FileHash $cached -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256 -or (Get-Item $cached).Length -ne $entry.bytes) { throw 'Cached model integrity mismatch.' }
    Copy-Item $cached "$output/models/$includedModel"
}
Copy-Item "$root/models/manifest.json" "$output/models/manifest.json"
# Tell the app which bundled candidate this package contains.
@{ Model=$Model; Language='auto'; Threads=2; Microphone=0; PushToTalk=$true; Key=20 } | ConvertTo-Json | Set-Content "$output/settings.json" -Encoding utf8NoBOM
Copy-Item "$vendor/LICENSE" "$output/licenses/whisper.cpp-MIT.txt"
Copy-Item "$root/THIRD_PARTY_NOTICES.md" "$output/licenses/"
Copy-Item "$root/LICENSE" "$output/licenses/LICENSE"
Copy-Item "$root/LICENSE-SCOPE.md" "$output/licenses/LICENSE-SCOPE.md"
Copy-Item "$root/docs/PRIVACY.md" "$output/PRIVACY.md"
Copy-Item "$root/licenses/*" "$output/licenses/"
# Preserve dependency licenses distributed by NuGet.
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
Get-ChildItem "$packageRoot/naudio*" -Recurse -File | Where-Object { $_.Name -match '^licen[cs]e' } | ForEach-Object {
    $name = ($_.FullName.Substring($packageRoot.Length) -replace '[/\\:]','_')
    Copy-Item $_.FullName "$output/licenses/$name"
}
Get-ChildItem "$packageRoot/microsoft.netcore.app.runtime.win-x64","$packageRoot/microsoft.windowsdesktop.app.runtime.win-x64" -Recurse -File | Where-Object { $_.Name -match '^(licen[cs]e|third.party.notices)' } | ForEach-Object {
    $name = ($_.FullName.Substring($packageRoot.Length) -replace '[/\\:]','_')
    Copy-Item $_.FullName "$output/licenses/$name"
}
if (-not (Test-Path "$output/runtime/JLVoiceAsr.exe")) { throw 'Missing packaged runtime.' }
Copy-Item "$root/docs/TESTING.md" "$output/TESTING.md"
Copy-Item "$root/docs/NVIDIA-APP-TEST.md" "$output/NVIDIA-APP-TEST.md"
Copy-Item "$root/docs/KNOWN_ISSUES.md" "$output/KNOWN_ISSUES.md"
Copy-Item "$root/scripts/check-package.ps1" "$output/Check-Package.ps1"
$sourceCommit = git -C $root rev-parse HEAD
Check-Exit
@{ source_commit=$sourceCommit; smart_app_control_qualified=$false; built_utc=[DateTime]::UtcNow.ToString('o'); model=$Model; runtime='win-x64'; framework='WPF'; profile='Offline CPU default; optional NVIDIA cloud development test'; release=$false } | ConvertTo-Json | Set-Content "$output/BUILD-INFO.json" -Encoding utf8NoBOM
& "$PSScriptRoot/finalize-package.ps1" -PackageDirectory $output
Write-Output "Development package created: $output. Windows acceptance testing is still required."
