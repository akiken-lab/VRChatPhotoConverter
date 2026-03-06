param(
    [string]$Configuration = "Release"
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

Write-Host "Publish completed: .\publish\win-x64"
Write-Host "Inno Setup Compiler で installer\GamePhotoAutoConverter.iss をビルドしてください。"
