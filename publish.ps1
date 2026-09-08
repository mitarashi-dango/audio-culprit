param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $PSScriptRoot
try {
 $publishArgs = @('publish', 'src/AudioCulprit', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', 'artifacts/single-file', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=false', '-p:DebugType=None', '-p:UseSharedCompilation=false', '-m:1')
 if ($NoRestore) { $publishArgs += '--no-restore' }
 dotnet @publishArgs
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
 Copy-Item artifacts/single-file/AudioCulprit.exe artifacts/AudioCulprit.exe -Force
 [xml]$project = Get-Content src/AudioCulprit/AudioCulprit.csproj -Raw
 $version = [string]$project.Project.PropertyGroup.Version
 if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected release version: $version" }
 $packageName = "AudioCulprit-$version-win-x64"
 # A fresh staging directory prevents stale files or local settings entering a release.
 $staging = Join-Path $PSScriptRoot ('artifacts/packages/' + [guid]::NewGuid().ToString('N'))
 $package = Join-Path $staging $packageName
 New-Item -ItemType Directory -Path $package -Force | Out-Null
 Copy-Item artifacts/AudioCulprit.exe (Join-Path $package 'AudioCulprit.exe')
 Copy-Item docs/GETTING-STARTED.txt (Join-Path $package 'README.txt')
 Copy-Item THIRD-PARTY-NOTICES.md $package
 Copy-Item licenses $package -Recurse
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $temporaryZip = Join-Path $staging "$packageName.zip"
 [IO.Compression.ZipFile]::CreateFromDirectory($package, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $true)
 $zip = Join-Path $PSScriptRoot "artifacts/$packageName.zip"
 Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
 Write-Output "Ready: $zip"
} finally { Pop-Location }
