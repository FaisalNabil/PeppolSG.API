Param(
    [string]$Configuration = "Release"
)
Write-Host "Building PeppolSG.API ($Configuration)"

$solution = "PeppolSG.API.sln"
msbuild $solution /p:Configuration=$Configuration /t:Clean,Build
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$packageDir = "artifacts"
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$package = "$packageDir/PeppolSG.API-$timestamp.zip"

Write-Host "Packaging into $package..."
Compress-Archive -Path PeppolSG.API/* -DestinationPath $package -Force

Write-Host "Package created: $package" 