#Requires -Version 5.1
<#  build-exe.ps1  -  Genera TOJI.exe desde el launcher usando ps2exe.
    Ejecutalo en Windows (PowerShell como admin, en esta carpeta):  .\build-exe.ps1
    Requiere internet la primera vez (para instalar el modulo ps2exe).
    IMPORTANTE: el .exe necesita seguir junto a toji-core.ps1, tweaks.json, etc.  #>

if (-not (Get-Module -ListAvailable -Name ps2exe)) {
    Write-Host "Instalando modulo ps2exe..." -ForegroundColor Cyan
    Install-Module ps2exe -Scope CurrentUser -Force
}
Import-Module ps2exe

$src = Join-Path $PSScriptRoot 'TOJI-GUI.ps1'
$out = Join-Path $PSScriptRoot 'TOJI.exe'
$ico = Join-Path $PSScriptRoot 'toji.ico'

$args = @{
    inputFile   = $src
    outputFile  = $out
    noConsole   = $true
    requireAdmin= $true
    title       = 'TOJI'
    product     = 'TOJI Optimizer'
    description = 'Optimizador de sistema'
}
if (Test-Path $ico) { $args.iconFile = $ico }   # opcional: pon un toji.ico en la carpeta

Invoke-PS2EXE @args
Write-Host "`nListo: $out" -ForegroundColor Green
Write-Host "Recuerda: manten el .exe junto a los .json y toji-core.ps1." -ForegroundColor Yellow
