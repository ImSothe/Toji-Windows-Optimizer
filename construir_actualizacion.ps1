# construir_actualizacion.ps1
# Compila Sothe Optimizer en Release (un solo .exe) y FIRMA la release. Deja en una carpeta los 3 ficheros
# que hay que subir a GitHub: SotheOptimizer.exe, release.json y release.sig.
#
# Uso (PowerShell, desde la raiz del repo):
#   .\construir_actualizacion.ps1 -Version 1.1.0 -Pem C:\claves-sothe\ota-private.pem -Notes "Novedades..."
#
# La clave privada (.pem) es tuya y NUNCA va al repo. Con ella se firma; la app solo instala binarios cuya
# firma valide con la clave publica incrustada (SignedData.PublicKey).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [Parameter(Mandatory = $true)] [string] $Pem,
    [string] $Notes = "",
    [string] $Out = "actualizacion"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repo

# --- Validaciones ---
$parsed = $null
if (-not [version]::TryParse($Version, [ref]$parsed)) {
    throw "Version invalida: '$Version'. Usa algo como 1.1.0"
}
if (-not (Test-Path $Pem)) {
    throw "No encuentro la clave privada: $Pem"
}
$proj = "src\SotheOptimizer\SotheOptimizer.csproj"
$csproj = Get-Content $proj -Raw
$actual = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if ($actual -eq $Version) {
    Write-Warning "El .csproj ya tiene <Version>$Version</Version>. Si es una release nueva, sube el numero."
}

Write-Host "== 1/3  Compilando Release v$Version (un solo .exe)..." -ForegroundColor Cyan
dotnet publish $proj -c Release -p:Version=$Version --nologo | Out-Host
$exe = "src\SotheOptimizer\bin\Release\net8.0-windows\win-x64\publish\SotheOptimizer.exe"
if (-not (Test-Path $exe)) { throw "No se genero el .exe en $exe" }

Write-Host "== 2/3  Firmando la release..." -ForegroundColor Cyan
dotnet run --project tools\OtaSigner -c Release --nologo -- sign-app $exe $Version $Pem $Notes | Out-Host
$dir = Split-Path -Parent $exe
foreach ($f in @("release.json", "release.sig")) {
    if (-not (Test-Path (Join-Path $dir $f))) { throw "No se genero $f (revisa la clave .pem)" }
}

Write-Host "== 3/3  Copiando los 3 ficheros a '$Out'..." -ForegroundColor Cyan
$outDir = Join-Path $repo $Out
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null
Copy-Item $exe, (Join-Path $dir "release.json"), (Join-Path $dir "release.sig") $outDir

$sha = (Get-FileHash (Join-Path $outDir "SotheOptimizer.exe") -Algorithm SHA256).Hash
Write-Host ""
Write-Host "LISTO. Sube estos 3 ficheros a una release '$("v$Version")' de tu repo PUBLICO de descargas:" -ForegroundColor Green
Get-ChildItem $outDir | ForEach-Object { Write-Host ("   " + $_.Name) }
Write-Host ""
Write-Host "SHA-256 del .exe: $sha"
Write-Host "Carpeta:          $outDir"
