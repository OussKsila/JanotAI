# ============================================================
#  build-mac-dist.ps1
#  Builds JanotAI for macOS (Apple Silicon) and packages it
#  as a self-contained ZIP ready for distribution.
#  No .NET installation required on the target Mac.
#
#  Usage : Right-click → Run with PowerShell
# ============================================================

$ErrorActionPreference = "Stop"
$Root    = $PSScriptRoot
$Dist    = "$Root\dist-mac"
$Runtime = "osx-arm64"
$Zip     = "$env:USERPROFILE\Desktop\janotai-mac.zip"

Write-Host ""
Write-Host "=== JanotAI — macOS Distribution Build ===" -ForegroundColor Cyan
Write-Host "  Target : $Runtime (Apple Silicon M1/M2/M3/M4)"
Write-Host ""

# ── Clean ───────────────────────────────────────────────────
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Dist | Out-Null

# ── 1. Build main binary ─────────────────────────────────────
Write-Host "[1/4] Building janotai..." -ForegroundColor Yellow
dotnet publish "$Root\JanotAi\JanotIA.csproj" `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o "$Dist" --nologo

# ── 2. Build ShellMcpServer ──────────────────────────────────
Write-Host "[2/4] Building shell-mcp..." -ForegroundColor Yellow
dotnet publish "$Root\ShellMcpServer\ShellMcpServer.csproj" `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:AssemblyName=shell-mcp `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o "$Dist" --nologo

# ── 3. Copy resources ────────────────────────────────────────
Write-Host "[3/4] Copying resources..." -ForegroundColor Yellow

if (Test-Path "$Root\installer\appsettings.mac.json") {
    Copy-Item "$Root\installer\appsettings.mac.json" "$Dist\appsettings.json" -Force
} else {
    Copy-Item "$Root\JanotAi\appsettings.json" "$Dist\appsettings.json" -Force
}

$wikiSrc = "$Root\JanotAi\wiki"
if (Test-Path $wikiSrc) {
    Copy-Item $wikiSrc "$Dist\wiki" -Recurse -Force
}

Copy-Item "$Root\install-mac.command" "$Dist\install-mac.command" -Force
Copy-Item "$Root\janot.command"       "$Dist\janot.command"       -Force
Copy-Item "$Root\LIRE-MOI-MAC.txt"   "$Dist\LIRE-MOI-MAC.txt"    -Force

# ── 4. Create ZIP ────────────────────────────────────────────
Write-Host "[4/4] Creating ZIP..." -ForegroundColor Yellow
if (Test-Path $Zip) { Remove-Item $Zip -Force }

Add-Type -Assembly System.IO.Compression.FileSystem
$zipFile = [System.IO.Compression.ZipFile]::Open($Zip, 'Create')

Get-ChildItem -Path $Dist -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($Dist.Length + 1)
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zipFile, $_.FullName, "janotai\$rel", 'Optimal') | Out-Null
}
$zipFile.Dispose()

$sizeMB = [math]::Round((Get-Item $Zip).Length / 1MB, 0)

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "  Output : $Zip ($sizeMB MB)" -ForegroundColor Green
Write-Host ""
