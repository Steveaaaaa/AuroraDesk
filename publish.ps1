$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$outDir = Join-Path $root "publish\AuroraDesk"
$stageRoot = Join-Path $root ("publish\Stage_" + [guid]::NewGuid().ToString("N"))
$stageDir = Join-Path $stageRoot "AuroraDesk"
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "=== 1. Running AuroraDesk.Tests ===" -ForegroundColor Cyan
dotnet run --project (Join-Path $root "tests\AuroraDesk.Tests\AuroraDesk.Tests.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "AuroraDesk.Tests failed ($LASTEXITCODE)." }

Write-Host "`n=== 2. Publishing Self-Contained Portable AuroraDesk.exe ===" -ForegroundColor Cyan
& (Join-Path $root "src\AuroraDesk.TaskbarTap\build_tap.ps1")
dotnet publish (Join-Path $root "src\AuroraDesk.App\AuroraDesk.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    --artifacts-path (Join-Path $stageRoot "artifacts") `
    -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "AuroraDesk publish failed ($LASTEXITCODE)." }

$tapDll = Join-Path $stageDir "AuroraDesk.TaskbarTap.dll"
if (-not (Test-Path -LiteralPath $tapDll -PathType Leaf)) { throw "Packaged taskbar component is missing." }

# Deploy to live publish\AuroraDesk while preserving Data & Backup (and tolerating locked TaskbarTap DLL)
Copy-Item (Join-Path $stageDir "AuroraDesk.exe") (Join-Path $outDir "AuroraDesk.exe") -Force
try {
    Copy-Item -LiteralPath $tapDll -Destination (Join-Path $outDir "AuroraDesk.TaskbarTap.dll") -Force
} catch {
    Write-Warning "Could not update the unpacked taskbar component: $_. The ZIP will contain the new component."
}

$exe = Get-Item (Join-Path $outDir "AuroraDesk.exe")
Write-Host ("Published portable single-file executable: {0} ({1} MB)" -f $exe.FullName, [math]::Round($exe.Length / 1MB, 2)) -ForegroundColor Green

$zipOut = Join-Path $root "publish\AuroraDesk_Portable_v1.0.zip"
if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
Compress-Archive -Path $stageDir -DestinationPath $zipOut -CompressionLevel Optimal
$resolvedStage = (Resolve-Path -LiteralPath $stageRoot).ProviderPath
$publishPrefix = (Resolve-Path -LiteralPath (Join-Path $root "publish")).ProviderPath.TrimEnd('\') + '\'
if ($resolvedStage.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
$zipInfo = Get-Item $zipOut
Write-Host ("Created distributable zip: {0} ({1} MB)" -f $zipInfo.FullName, [math]::Round($zipInfo.Length / 1MB, 2)) -ForegroundColor Green
