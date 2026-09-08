param(
 [string]$IdentityName,
 [string]$Publisher,
 [string]$PublisherDisplayName,
 [string]$DisplayName = 'AudioCulprit',
 [string]$MakeAppxPath,
 [switch]$PrepareOnly,
 [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$identityPath = Join-Path $PSScriptRoot 'packaging/store/identity.json'
if (Test-Path -LiteralPath $identityPath) {
 $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
 if (!$IdentityName) { $IdentityName = $identity.IdentityName }
 if (!$Publisher) { $Publisher = $identity.Publisher }
 if (!$PublisherDisplayName) { $PublisherDisplayName = $identity.PublisherDisplayName }
}
if (!$IdentityName -or !$Publisher -or !$PublisherDisplayName) {
 if (!$PrepareOnly) { throw 'Supply IdentityName, Publisher and PublisherDisplayName from Partner Center. Use -PrepareOnly for a local draft.' }
 $IdentityName = 'AudioCulprit.LocalDraft'
 $Publisher = 'CN=AudioCulprit-LocalDraft'
 $PublisherDisplayName = 'AudioCulprit (local draft)'
}
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package identity name.' }
if (!$Publisher.StartsWith('CN=')) { throw 'Publisher must be the exact CN= value from Partner Center.' }
if (!$PrepareOnly -and !$MakeAppxPath) {
 $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
 $MakeAppxPath = Get-ChildItem "$sdk/*/x64/makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$PrepareOnly -and (!$MakeAppxPath -or !(Test-Path -LiteralPath $MakeAppxPath))) { throw 'Install Windows SDK (MSIX Packaging Tools), or supply -MakeAppxPath.' }
Push-Location $PSScriptRoot
try {
 [xml]$project = Get-Content src/AudioCulprit/AudioCulprit.csproj -Raw
 $version = [string]$project.Project.PropertyGroup.Version
 if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project version.' }
 $stage = Join-Path $PSScriptRoot ('artifacts/store/' + [guid]::NewGuid().ToString('N'))
 $payload = Join-Path $stage 'payload'
 $publishArgs = @('publish', 'src/AudioCulprit', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $payload, '-p:StoreBuild=true', '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:UseSharedCompilation=false', '-m:1')
 if ($NoRestore) { $publishArgs += '--no-restore' }
 dotnet @publishArgs
 if ($LASTEXITCODE -ne 0) { throw 'Store publish failed.' }
 Copy-Item THIRD-PARTY-NOTICES.md $payload
 Copy-Item licenses $payload -Recurse
 $assets = New-Item -ItemType Directory -Path (Join-Path $payload 'Assets') -Force
 Add-Type -AssemblyName System.Drawing
 $source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'src/AudioCulprit/Assets/audio-culprit-source.png'))
 try {
  foreach ($size in @(50, 44, 150)) {
   $bitmap = New-Object Drawing.Bitmap($size, $size)
   $graphics = [Drawing.Graphics]::FromImage($bitmap)
   try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $ratio = [Math]::Min($size / $source.Width, $size / $source.Height)
    $width = [int]($source.Width * $ratio); $height = [int]($source.Height * $ratio)
    $graphics.DrawImage($source, [int](($size-$width)/2), [int](($size-$height)/2), $width, $height)
    $bitmap.Save((Join-Path $assets.FullName "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png)
   } finally { $graphics.Dispose(); $bitmap.Dispose() }
  }
 } finally { $source.Dispose() }
 $manifest = Get-Content packaging/store/AppxManifest.xml -Raw
 foreach ($pair in @(@('IDENTITY_NAME',$IdentityName), @('PUBLISHER',$Publisher), @('PUBLISHER_DISPLAY_NAME',$PublisherDisplayName), @('DISPLAY_NAME',$DisplayName), @('VERSION',"$version.0"))) {
  $manifest = $manifest.Replace('{{'+$pair[0]+'}}', [Security.SecurityElement]::Escape($pair[1]))
 }
 [xml]$validatedXml = $manifest
 [IO.File]::WriteAllText((Join-Path $payload 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
 if ($PrepareOnly) { Write-Output "Draft payload only (not a Store submission): $payload"; return }
 $output = Join-Path $stage "AudioCulprit-$version-x64.msix"
 & $MakeAppxPath pack /d $payload /p $output /o
 if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging/validation failed.' }
 Write-Output "Unsigned Store submission package: $output"
} finally { Pop-Location }
