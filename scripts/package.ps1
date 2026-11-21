param([ValidateSet('ggml-base-q5_1.bin','ggml-base-q8_0.bin','ggml-small-q5_1.bin')][string]$Model = 'ggml-base-q5_1.bin')
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
$entry = $manifest.models | Where-Object file -eq $Model
$cached = Join-Path $root "models/$Model"
if (-not (Test-Path $cached)) {
    Invoke-WebRequest "https://huggingface.co/$($manifest.repository)/resolve/$($manifest.revision)/$Model" -OutFile "$cached.part"
    if ((Get-FileHash "$cached.part" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Model download hash mismatch.' }
    Move-Item "$cached.part" $cached
}
if ((Get-FileHash $cached -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Cached model hash mismatch.' }
Copy-Item $cached "$output/models/$Model"
# Tell the app which bundled candidate this package contains.
@{ Model=$Model; Language='auto'; Threads=2; Microphone=0; PushToTalk=$false; Key=120 } | ConvertTo-Json | Set-Content "$output/settings.json" -Encoding utf8NoBOM
Copy-Item "$vendor/LICENSE" "$output/licenses/whisper.cpp-MIT.txt"
Copy-Item "$root/THIRD_PARTY_NOTICES.md" "$output/licenses/"
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
Get-ChildItem $output -Recurse -File | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($output.Length+1); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
} | ConvertTo-Json | Set-Content "$output/SHA256SUMS.json" -Encoding utf8NoBOM
Write-Output "Development package created: $output. Windows acceptance testing is still required."
