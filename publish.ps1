param()
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $PSScriptRoot
try {
 dotnet publish src/AudioCulprit -c Release -r win-x64 --self-contained true -o artifacts/single-file -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
 Copy-Item artifacts/single-file/AudioCulprit.exe artifacts/AudioCulprit.exe -Force
 Write-Output "Ready: $PSScriptRoot\artifacts\AudioCulprit.exe"
} finally { Pop-Location }
