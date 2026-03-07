param(
    [string]$Configuration = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    throw "dotnet.exe が見つかりません: $dotnet"
}

& $dotnet publish .\src\App.Wpf\App.Wpf.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o .\publish\win-x64

$distributionDir = Join-Path (Get-Location) "distribution"
if (Test-Path $distributionDir) {
    $readmeSrc = Join-Path $distributionDir "readme.txt"
    $licenseSrc = Join-Path $distributionDir "license.txt"
    if (Test-Path $readmeSrc) {
        Copy-Item -Path $readmeSrc -Destination ".\publish\win-x64\readme.txt" -Force
    }
    if (Test-Path $licenseSrc) {
        Copy-Item -Path $licenseSrc -Destination ".\publish\win-x64\license.txt" -Force
    }
}

$publishDir = Resolve-Path .\publish\win-x64
Write-Host "Publish completed: $publishDir"
Write-Host "Portable distribution directory is ready."

if (-not $NoZip) {
    $distDir = Join-Path (Get-Location) "dist"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null

    $zipPath = Join-Path $distDir "VRCJpegAutoGenerator-portable.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    $stagingRoot = Join-Path $distDir "_portable_staging"
    $packageRoot = Join-Path $stagingRoot "VRCJpegAutoGenerator"
    if (Test-Path $stagingRoot) {
        Remove-Item $stagingRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    Copy-Item -Path ".\publish\win-x64\*" -Destination $packageRoot -Recurse -Force
    Get-ChildItem -Path $packageRoot -Filter *.pdb -File -Recurse | Remove-Item -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $zipPath)
    Remove-Item $stagingRoot -Recurse -Force
    Write-Host "Portable ZIP created: $zipPath"
}

