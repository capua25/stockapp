#Requires -Version 5.1
<#
.SYNOPSIS
    Empaqueta StockApp para Windows usando dotnet publish + vpk (Velopack).

.DESCRIPTION
    1. Publica la app como self-contained para win-x64.
    2. Llama a `vpk pack` para generar Setup.exe + paquetes full/delta.
    La version se lee del .csproj de Presentation (single source of truth).
    Si hay una release previa en releases/win, vpk genera delta automaticamente.

.PARAMETER Version
    Opcional. Sobreescribe la version leida del .csproj.
    Usar solo si se necesita empaquetar una version distinta a la del csproj.

.EXAMPLE
    .\build\pack-win.ps1
    .\build\pack-win.ps1 -Version 1.0.0

.NOTES
    Prerequisitos:
      - .NET 10 SDK instalado (dotnet --version)
      - vpk global tool instalado: dotnet tool install -g vpk
    Ejecutar desde la raiz del repositorio.
    Validacion manual obligatoria: ver build/README-empaquetado.md
#>
param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Configuracion ────────────────────────────────────────────────────────────
$CsprojPath   = "src\StockApp.Presentation\StockApp.Presentation.csproj"
$PublishDir   = "publish\win-x64"
$OutputDir    = "releases\win"
$ReleaseNotes = "build\RELEASE_NOTES.md"
$PackId       = "StockApp"
$Channel      = "win"
$MainExe      = "StockApp.Presentation.exe"
$Runtime      = "win-x64"

# ── Funciones auxiliares ──────────────────────────────────────────────────────
function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Abort([string]$msg) {
    Write-Host ""
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

# ── Verificacion de prerequisitos ────────────────────────────────────────────
Write-Step "Verificando prerequisitos..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Abort ".NET SDK no encontrado. Instala .NET 10 SDK desde https://dot.net"
}

$dotnetVersion = dotnet --version
Write-Host "  dotnet: $dotnetVersion"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Abort "vpk no encontrado. Instala con: dotnet tool install -g vpk"
}

$vpkVersion = (Get-Command vpk).Source
Write-Host "  vpk: $vpkVersion"

if (-not (Test-Path $CsprojPath)) {
    Abort "No se encontro el csproj en: $CsprojPath"
}

if (-not (Test-Path $ReleaseNotes)) {
    Abort "No se encontro el archivo de release notes en: $ReleaseNotes"
}

# ── Leer version del .csproj (single source of truth) ───────────────────────
if ($Version -eq "") {
    Write-Step "Leyendo version del .csproj..."
    [xml]$csproj = Get-Content $CsprojPath
    $Version = $csproj.Project.PropertyGroup |
        Where-Object { $_.Version } |
        Select-Object -First 1 -ExpandProperty Version
    if (-not $Version) {
        Abort "No se encontro <Version> en $CsprojPath. Agregala antes de empaquetar."
    }
}

Write-Host "  Version a empaquetar: $Version"

# ── Dotnet publish ────────────────────────────────────────────────────────────
Write-Step "Publicando para $Runtime (self-contained)..."

dotnet publish $CsprojPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) { Abort "dotnet publish fallo (codigo $LASTEXITCODE)." }

Write-Host "  Publicado en: $PublishDir"

# ── vpk pack ─────────────────────────────────────────────────────────────────
Write-Step "Empaquetando con vpk (channel: $Channel, delta: BestSpeed)..."

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

vpk pack `
    --packId       $PackId   `
    --packVersion  $Version  `
    --packDir      $PublishDir `
    --mainExe      $MainExe  `
    --channel      $Channel  `
    --delta        BestSpeed `
    --releaseNotes $ReleaseNotes `
    --outputDir    $OutputDir
    # --signParams "/a /fd sha256 /td sha256 /f cert.pfx /p $env:CERT_PWD"
    # D7: code signing fuera de alcance en Fase A.
    # Cuando haya certificado, descomentar la linea anterior y configurar CERT_PWD.

if ($LASTEXITCODE -ne 0) { Abort "vpk pack fallo (codigo $LASTEXITCODE)." }

# ── Resultado ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Empaquetado completado exitosamente." -ForegroundColor Green
Write-Host ""
# NOTA: confirmar los nombres reales de los .nupkg al empaquetar (V1); vpk incluye el channel en el nombre (ej: StockApp-X.Y.Z-win-full.nupkg).
Write-Host "Artefactos generados en: $OutputDir"
Write-Host "  Setup.exe               -> instalador para el usuario final"
Write-Host "  StockApp-$Version-full.nupkg -> paquete completo"
Write-Host "  StockApp-$Version-delta.nupkg -> paquete delta (si habia release previa)"
Write-Host "  releases.win.json       -> feed de updates para Velopack"
Write-Host ""
Write-Host "Siguiente paso: subir el contenido de $OutputDir a un GitHub Release." -ForegroundColor Yellow
Write-Host "  Ver build\README-empaquetado.md para el flujo completo."
