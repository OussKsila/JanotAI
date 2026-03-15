# ============================================================
#  build-installer.ps1 — Build + package Janot.ia pour Windows
#  Usage : .\build-installer.ps1
#  Prérequis : .NET 8 SDK, Inno Setup 6 (optionnel pour le .exe)
# ============================================================

$ErrorActionPreference = "Stop"
$Root    = $PSScriptRoot
$Dist    = "$Root\installer\dist"
$Runtime = "win-x64"

Write-Host "`n=== Janot.ia — Build Installer ===" -ForegroundColor Cyan

# ── 0. Convertir l'icône PNG → ICO ───────────────────────────
$IcoPng = "$Root\installer\janot.png"
$IcoFile = "$Root\installer\janot.ico"
if (Test-Path $IcoPng) {
    Write-Host "`n[0/3] Conversion de l'icône PNG → ICO..." -ForegroundColor Yellow
    & "$Root\installer\convert-icon.ps1" -Source $IcoPng -Destination $IcoFile
} elseif (-not (Test-Path $IcoFile)) {
    Write-Warning "Aucune icône trouvée (installer\janot.png ou janot.ico). L'exe sera sans icône personnalisée."
}

# ── Nettoyer le dossier dist ─────────────────────────────────
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Dist | Out-Null

# ── 1. Publier le projet principal (janot.exe) ───────────────
Write-Host "`n[1/3] Publication de janot.exe..." -ForegroundColor Yellow
dotnet publish "$Root\SemanticKernelAgentsDemo\JanotIA.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$Dist"

# ── 2. Publier ShellMcpServer ────────────────────────────────
Write-Host "`n[2/3] Publication de ShellMcpServer.exe..." -ForegroundColor Yellow
dotnet publish "$Root\ShellMcpServer" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$Dist"

# ── 3. Copier les fichiers de config ─────────────────────────
Write-Host "`n[3/3] Copie des fichiers de config..." -ForegroundColor Yellow
Copy-Item "$Root\installer\appsettings.dist.json" "$Dist\appsettings.json" -Force

# Contacts vide pour la distribution
'{ "contacts": [] }' | Out-File "$Dist\contacts.json" -Encoding utf8 -Force

# ── Résultat ─────────────────────────────────────────────────
Write-Host "`n=== Fichiers dans $Dist ===" -ForegroundColor Green
Get-ChildItem $Dist | Format-Table Name, Length -AutoSize

# ── Compiler le setup.exe avec Inno Setup (si installé) ──────
$InnoSetup = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($InnoSetup) {
    Write-Host "`nCompilation du setup.exe avec Inno Setup..." -ForegroundColor Cyan
    & $InnoSetup "$Root\installer\setup.iss"
    Write-Host "`n✓ setup.exe créé dans installer\Output\" -ForegroundColor Green
} else {
    Write-Host "`n[!] Inno Setup non trouvé — installer/dist/ est prêt." -ForegroundColor Yellow
    Write-Host "    Télécharge Inno Setup 6 sur : https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "    Puis relance ce script pour générer le setup.exe." -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Green
