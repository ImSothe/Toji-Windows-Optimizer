<#
================================================================================
  TOJI | Motor de optimizacion (core)
   - "no importa cuanta seguridad quites, siempre hay vuelta atras"
================================================================================
  Este archivo NO aplica nada por si solo. Expone funciones. Lo consume:
    - TOJI.ps1        (lanzador de consola)
    - la futura GUI en WPF (leera el MISMO tweaks.json)

  Filosofia de seguridad (lo que lo hace "endgame" sin ser suicida):
    1. Antes de tocar un valor, se LEE y se GUARDA el estado previo.
    2. Revert-Tweak restaura exactamente ese estado previo.
    3. Revert-All es el boton de panico: deshace TODO lo aplicado.
    4. Punto de restauracion opcional antes de cargar.
    5. Modo -DryRun: ensena que haria sin tocar nada.
================================================================================
#>

$script:TojiRoot    = Join-Path $env:ProgramData 'TOJI'
$script:TojiBackups = Join-Path $script:TojiRoot 'backups'
$script:TojiLog     = Join-Path $script:TojiRoot 'toji.log'

function Initialize-Toji {
    foreach ($d in @($script:TojiRoot, $script:TojiBackups)) {
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    }
}

function Write-TojiLog {
    param([string]$Message, [string]$Level = 'INFO')
    Initialize-Toji
    $line = "{0} [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Add-Content -Path $script:TojiLog -Value $line -Encoding UTF8
}

# ---------------------------------------------------------------- Administrador
function Test-TojiAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-TojiAdmin {
    if (-not (Test-TojiAdmin)) {
        throw "TOJI necesita ejecutarse como Administrador. Abre PowerShell con 'Ejecutar como administrador'."
    }
}

# ------------------------------------------------------------ Punto restauracion
function New-TojiRestorePoint {
    param([string]$Description = 'TOJI - antes de optimizar')
    try {
        # Windows limita a 1 punto cada 24h; quitamos ese limite.
        Set-TojiRegValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' `
            -Name 'SystemRestorePointCreationFrequency' -Value 0 -Kind DWord
        Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
        Checkpoint-Computer -Description $Description -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
        Write-TojiLog "Punto de restauracion creado: $Description"
        return $true
    } catch {
        Write-TojiLog "No se pudo crear punto de restauracion: $($_.Exception.Message)" 'WARN'
        return $false
    }
}

# ====================================================== HELPERS DE REGISTRO ====
function Convert-TojiRegPath {
    param([string]$Path)
    $map = @{
        'HKLM'='LocalMachine'; 'HKEY_LOCAL_MACHINE'='LocalMachine'
        'HKCU'='CurrentUser';  'HKEY_CURRENT_USER'='CurrentUser'
        'HKCR'='ClassesRoot';  'HKEY_CLASSES_ROOT'='ClassesRoot'
        'HKU' ='Users';        'HKEY_USERS'='Users'
        'HKCC'='CurrentConfig';'HKEY_CURRENT_CONFIG'='CurrentConfig'
    }
    $clean = $Path -replace ':',''
    $parts = $clean -split '\\', 2
    $hiveKey = $parts[0].ToUpper()
    if (-not $map.ContainsKey($hiveKey)) { throw "Hive no reconocido: $hiveKey" }
    [pscustomobject]@{
        Hive = [Microsoft.Win32.RegistryHive]$map[$hiveKey]
        Sub  = $parts[1]
    }
}

function Get-TojiRegValue {
    param($Hive, [string]$Sub, [string]$Name)
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $null
    try {
        $key = $base.OpenSubKey($Sub, $false)
        if (-not $key -or ($key.GetValueNames() -notcontains $Name)) {
            return [pscustomobject]@{ Exists = $false }
        }
        [pscustomobject]@{
            Exists = $true
            Value  = $key.GetValue($Name)
            Kind   = $key.GetValueKind($Name).ToString()
        }
    } finally { if ($key) { $key.Close() }; $base.Close() }
}

function Set-TojiRegValue {
    param($Path, $Hive, [string]$Sub, [string]$Name, $Value, [string]$Kind = 'DWord')
    if ($Path) { $p = Convert-TojiRegPath $Path; $Hive = $p.Hive; $Sub = $p.Sub }
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $null
    try {
        $key = $base.CreateSubKey($Sub)
        $vk  = [Microsoft.Win32.RegistryValueKind]$Kind
        switch ($Kind) {
            'DWord'       { $key.SetValue($Name, [int32][int64]"$Value", $vk) }
            'QWord'       { $key.SetValue($Name, [int64]"$Value", $vk) }
            'Binary'      { $key.SetValue($Name, [byte[]]$Value, $vk) }
            'MultiString' { $key.SetValue($Name, [string[]]$Value, $vk) }
            default       { $key.SetValue($Name, [string]$Value, $vk) }
        }
    } finally { if ($key) { $key.Close() }; $base.Close() }
}

function Remove-TojiRegValue {
    param($Hive, [string]$Sub, [string]$Name)
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $null
    try {
        $key = $base.OpenSubKey($Sub, $true)
        if ($key -and ($key.GetValueNames() -contains $Name)) { $key.DeleteValue($Name, $false) }
    } finally { if ($key) { $key.Close() }; $base.Close() }
}

# ================================================ APLICAR / DESHACER POR OP =====
function Backup-Path { param([string]$Id) Join-Path $script:TojiBackups ("{0}.json" -f $Id) }

function Apply-Tweak {
    param([Parameter(Mandatory)]$Tweak, [switch]$DryRun)
    Initialize-Toji
    $backup = @()
    $errors = @()

    foreach ($op in $Tweak.ops) {
        try {
            switch ($op.type) {

                'reg' {
                    $p = Convert-TojiRegPath $op.path
                    $prior = Get-TojiRegValue $p.Hive $p.Sub $op.name
                    if ($DryRun) {
                        Write-Host "   [reg] $($op.path)\$($op.name) => $($op.value)" -ForegroundColor DarkGray
                    } else {
                        $kind = if ($op.kind) { $op.kind } else { 'DWord' }
                        Set-TojiRegValue -Path $op.path -Name $op.name -Value $op.value -Kind $kind
                    }
                    $backup += @{ type='reg'; path=$op.path; name=$op.name; prior=$prior }
                }

                'service' {
                    $svc = Get-CimInstance Win32_Service -Filter "Name='$($op.name)'" -ErrorAction SilentlyContinue
                    if (-not $svc) { $errors += "Servicio inexistente: $($op.name)"; break }
                    $backup += @{ type='service'; name=$op.name; prior=@{ StartMode=$svc.StartMode; State=$svc.State } }
                    if (-not $DryRun) {
                        Set-Service -Name $op.name -StartupType $op.startup -ErrorAction Stop
                        if ($op.startup -eq 'Disabled') { Stop-Service -Name $op.name -Force -ErrorAction SilentlyContinue }
                    } else {
                        Write-Host "   [service] $($op.name) => $($op.startup)" -ForegroundColor DarkGray
                    }
                }

                'appx' {
                    $backup += @{ type='appx'; package=$op.package; revertible=$false }
                    if (-not $DryRun) {
                        Get-AppxPackage -Name $op.package -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
                        if (Test-TojiAdmin) {
                            Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue |
                                Where-Object DisplayName -like $op.package |
                                ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null }
                        }
                    } else {
                        Write-Host "   [appx] eliminar $($op.package)  (NO auto-reversible)" -ForegroundColor DarkGray
                    }
                }

                'command' {
                    $backup += @{ type='command'; revert=$op.revert }
                    if (-not $DryRun) {
                        if ($op.apply) { Invoke-Expression $op.apply | Out-Null }
                    } else {
                        Write-Host "   [cmd] $($op.apply)" -ForegroundColor DarkGray
                    }
                }

                default { $errors += "Tipo de op desconocido: $($op.type)" }
            }
        } catch {
            $errors += "op $($op.type): $($_.Exception.Message)"
        }
    }

    if (-not $DryRun) {
        $backup | ConvertTo-Json -Depth 8 | Set-Content -Path (Backup-Path $Tweak.id) -Encoding UTF8
        Write-TojiLog "APLICADO $($Tweak.id)$(if($errors){' | errores: '+($errors -join '; ')})"
    }
    [pscustomobject]@{ Id=$Tweak.id; Ok=($errors.Count -eq 0); Errors=$errors }
}

function Revert-Tweak {
    param([Parameter(Mandatory)][string]$Id)
    $bp = Backup-Path $Id
    if (-not (Test-Path $bp)) { return [pscustomobject]@{ Id=$Id; Ok=$false; Errors=@('sin backup: nunca se aplico') } }
    $ops = Get-Content $bp -Raw | ConvertFrom-Json
    $errors = @()
    $svcMap = @{ Auto='Automatic'; Manual='Manual'; Disabled='Disabled'; Boot='Boot'; System='System' }

    foreach ($b in @($ops)) {
        try {
            switch ($b.type) {
                'reg' {
                    $p = Convert-TojiRegPath $b.path
                    if ($b.prior.Exists) { Set-TojiRegValue -Hive $p.Hive -Sub $p.Sub -Name $b.name -Value $b.prior.Value -Kind $b.prior.Kind }
                    else { Remove-TojiRegValue $p.Hive $p.Sub $b.name }
                }
                'service' {
                    $st = $svcMap[[string]$b.prior.StartMode]; if (-not $st) { $st = 'Manual' }
                    Set-Service -Name $b.name -StartupType $st -ErrorAction Stop
                    if ($b.prior.State -eq 'Running') { Start-Service -Name $b.name -ErrorAction SilentlyContinue }
                }
                'appx'    { $errors += "appx $($b.package): reinstalar desde Microsoft Store (no auto-reversible)" }
                'command' { if ($b.revert) { Invoke-Expression $b.revert | Out-Null } else { $errors += 'command sin revert definido' } }
            }
        } catch { $errors += "$($b.type): $($_.Exception.Message)" }
    }
    Remove-Item $bp -Force -ErrorAction SilentlyContinue
    Write-TojiLog "REVERTIDO $Id$(if($errors){' | notas: '+($errors -join '; ')})"
    [pscustomobject]@{ Id=$Id; Ok=($errors.Count -eq 0); Errors=$errors }
}

function Get-TojiApplied {
    if (-not (Test-Path $script:TojiBackups)) { return @() }
    Get-ChildItem $script:TojiBackups -Filter *.json -ErrorAction SilentlyContinue |
        ForEach-Object { $_.BaseName }
}

function Revert-All {
    $applied = Get-TojiApplied
    if (-not $applied) { Write-Host "No hay nada aplicado por TOJI." -ForegroundColor Yellow; return }
    foreach ($id in $applied) { Revert-Tweak -Id $id | Out-Null }
    Write-Host "PANICO: revertidos $($applied.Count) tweaks. Reinicia para que todo asiente." -ForegroundColor Green
}

# ===================================================== LIBRERIA DE TWEAKS =======
function Get-TojiTweaks {
    param([string]$JsonPath = (Join-Path $PSScriptRoot 'tweaks.json'))
    if (-not (Test-Path $JsonPath)) { throw "No encuentro tweaks.json en $JsonPath" }
    (Get-Content $JsonPath -Raw | ConvertFrom-Json).tweaks
}

# ============================================= CODIGO EXPORT / IMPORT ===========
# Formato compatible con el prototipo web:  TOJI-1-<base64 de {"v":1,"t":[ids]}>
function Export-TojiCode {
    param([string[]]$Ids)
    if (-not $Ids) { $Ids = Get-TojiApplied }
    $json  = (@{ v = 1; t = @($Ids) } | ConvertTo-Json -Compress)
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    'TOJI-1-' + [Convert]::ToBase64String($bytes)
}

function Import-TojiCode {
    param([Parameter(Mandatory)][string]$Code)
    $b64 = ($Code.Trim() -replace '^TOJI-1-', '')
    $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
    $obj = $json | ConvertFrom-Json
    if (-not $obj.t) { throw 'Codigo invalido' }
    @($obj.t)
}

# ================================================= LIMPIEZA (basura) ============
function Clear-TojiTemp {
    $targets = @(
        "$env:TEMP\*",
        "$env:SystemRoot\Temp\*",
        "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db"
    )
    foreach ($t in $targets) {
        Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
    }
    try { Clear-RecycleBin -Force -ErrorAction SilentlyContinue } catch {}
    # Cache de Windows Update (requiere parar el servicio)
    try {
        Stop-Service wuauserv -Force -ErrorAction SilentlyContinue
        Remove-Item "$env:SystemRoot\SoftwareDistribution\Download\*" -Recurse -Force -ErrorAction SilentlyContinue
        Start-Service wuauserv -ErrorAction SilentlyContinue
    } catch {}
    Write-TojiLog 'Limpieza de temporales ejecutada'
    Write-Host "Basura eliminada (temp, papelera, cache de Windows Update)." -ForegroundColor Green
}

# ===================================================== PERFILES ================
function Get-TojiProfiles {
    param([string]$JsonPath = (Join-Path $PSScriptRoot 'profiles.json'))
    if (-not (Test-Path $JsonPath)) { return @() }
    (Get-Content $JsonPath -Raw | ConvertFrom-Json).profiles
}

# Deja el sistema EXACTAMENTE con los tweaks de $Ids:
# revierte lo aplicado que no este en $Ids, aplica lo de $Ids que falte.
function Set-TojiProfile {
    param([Parameter(Mandatory)]$Tweaks, [string[]]$Ids, [switch]$DryRun)
    $Ids = @($Ids)
    $applied  = @(Get-TojiApplied)
    $toRevert = $applied | Where-Object { $Ids -notcontains $_ }
    $toApply  = $Ids     | Where-Object { $applied -notcontains $_ }
    $res = [pscustomobject]@{ Reverted=@(); Applied=@(); Errors=@(); Reboot=$false }
    foreach ($id in $toRevert) {
        if ($DryRun) { $res.Reverted += $id; continue }
        $r = Revert-Tweak -Id $id; $res.Reverted += $id; if ($r.Errors) { $res.Errors += $r.Errors }
    }
    foreach ($id in $toApply) {
        $t = $Tweaks | Where-Object id -eq $id
        if (-not $t) { $res.Errors += "id desconocido: $id"; continue }
        if ($DryRun) { $res.Applied += $id; continue }
        $r = Apply-Tweak -Tweak $t; $res.Applied += $id
        if ($r.Errors) { $res.Errors += $r.Errors }
        if ($t.reboot) { $res.Reboot = $true }
    }
    Write-TojiLog ("PERFIL aplicado | revertidos: {0} | aplicados: {1}" -f $res.Reverted.Count, $res.Applied.Count)
    $res
}

# ==================================== WINGET (instalador de apps) ==============
function Test-TojiWinget { [bool](Get-Command winget -ErrorAction SilentlyContinue) }

function Get-TojiApps {
    param([string]$JsonPath = (Join-Path $PSScriptRoot 'apps.json'))
    if (-not (Test-Path $JsonPath)) { return @() }
    (Get-Content $JsonPath -Raw | ConvertFrom-Json).apps
}
function Install-TojiApps {
    param([string[]]$Ids)
    if (-not $Ids) { return }
    $cmds = ($Ids | ForEach-Object { "winget install --id $_ -e --accept-source-agreements --accept-package-agreements" }) -join " ; "
    $full = "Write-Host 'TOJI - instalando apps...' -ForegroundColor Green ; $cmds ; Write-Host '' ; Write-Host 'Terminado. Puedes cerrar esta ventana.' -ForegroundColor Green"
    Start-Process powershell -ArgumentList "-NoExit","-ExecutionPolicy","Bypass","-Command",$full
    Write-TojiLog ("WINGET install: " + ($Ids -join ', '))
}
function Uninstall-TojiApps {
    param([string[]]$Ids)
    if (-not $Ids) { return }
    $cmds = ($Ids | ForEach-Object { "winget uninstall --id $_ -e" }) -join " ; "
    $full = "Write-Host 'TOJI - desinstalando...' -ForegroundColor Yellow ; $cmds ; Write-Host 'Terminado.' -ForegroundColor Green"
    Start-Process powershell -ArgumentList "-NoExit","-ExecutionPolicy","Bypass","-Command",$full
    Write-TojiLog ("WINGET uninstall: " + ($Ids -join ', '))
}

# ==================================== CARACTERISTICAS DE WINDOWS ================
function Get-TojiFeatures {
    param([string]$JsonPath = (Join-Path $PSScriptRoot 'features.json'))
    if (-not (Test-Path $JsonPath)) { return @() }
    (Get-Content $JsonPath -Raw | ConvertFrom-Json).features
}
function Get-TojiFeatureState {
    param([string]$Name)
    try { (Get-WindowsOptionalFeature -Online -FeatureName $Name -ErrorAction Stop).State.ToString() } catch { 'Desconocido' }
}
function Set-TojiFeature {
    param([string]$Name, [bool]$Enable)
    if ($Enable) { Enable-WindowsOptionalFeature -Online -FeatureName $Name -All -NoRestart -ErrorAction Stop | Out-Null }
    else         { Disable-WindowsOptionalFeature -Online -FeatureName $Name -NoRestart -ErrorAction Stop | Out-Null }
    Write-TojiLog ("FEATURE {0} -> {1}" -f $Name, $(if ($Enable) { 'ON' } else { 'OFF' }))
}

# ==================================== REPARACIONES =============================
function Start-TojiConsole {
    param([string]$Command, [string]$Title = 'TOJI')
    $full = "`$host.UI.RawUI.WindowTitle='$Title' ; Write-Host '=== $Title ===' -ForegroundColor Green ; $Command ; Write-Host '' ; Write-Host 'Terminado. Puedes cerrar esta ventana.' -ForegroundColor Green"
    Start-Process powershell -ArgumentList "-NoExit","-ExecutionPolicy","Bypass","-Command",$full
}
function Repair-TojiSFC     { Start-TojiConsole -Title 'SFC scannow' -Command 'sfc /scannow' }
function Repair-TojiDISM    { Start-TojiConsole -Title 'DISM RestoreHealth' -Command 'DISM /Online /Cleanup-Image /RestoreHealth' }
function Repair-TojiChkdsk  { Start-TojiConsole -Title 'CHKDSK' -Command 'chkdsk C: /scan' }
function Repair-TojiNetwork { Start-TojiConsole -Title 'Reset de red' -Command 'netsh winsock reset ; netsh int ip reset ; ipconfig /flushdns ; ipconfig /release ; ipconfig /renew ; Write-Host "Reinicia el PC para completar el reset." -ForegroundColor Yellow' }
function Repair-TojiWU {
    Start-TojiConsole -Title 'Reparar Windows Update' -Command 'net stop wuauserv ; net stop bits ; net stop cryptsvc ; Rename-Item "$env:SystemRoot\SoftwareDistribution" SoftwareDistribution.old -ErrorAction SilentlyContinue ; Rename-Item "$env:SystemRoot\System32\catroot2" catroot2.old -ErrorAction SilentlyContinue ; net start wuauserv ; net start bits ; net start cryptsvc ; Write-Host "Cache de Windows Update reiniciada." -ForegroundColor Green'
}
function Repair-TojiFlushDNS { ipconfig /flushdns | Out-Null; Write-TojiLog 'Flush DNS' }

function Save-TojiProfile {
    param([Parameter(Mandatory)][string]$Name, [string[]]$Ids, [string]$Desc = 'Perfil personalizado',
          [string]$JsonPath = (Join-Path $PSScriptRoot 'profiles.json'))
    $id = ($Name.ToLower() -replace '[^a-z0-9]+','_').Trim('_'); if (-not $id) { $id = 'perfil' }
    if (Test-Path $JsonPath) { $doc = Get-Content $JsonPath -Raw | ConvertFrom-Json } else { $doc = [pscustomobject]@{ profiles = @() } }
    $list = @($doc.profiles | Where-Object { $_.id -ne $id })
    $list += [pscustomobject]@{ id = $id; name = $Name; desc = $Desc; tweaks = @($Ids) }
    [pscustomobject]@{ profiles = $list } | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding UTF8
    Write-TojiLog "Perfil guardado: $Name ($($Ids.Count) tweaks)"
    $id
}

# ==================================== TIMER RESOLUTION (residente ligero) =======
# Compila un .exe nativo minusculo (pocos MB de RAM, sin ventana) que sujeta el
# temporizador. Mucho mas ligero que dejar un powershell residente.
function Install-TojiTimerRes {
    if (-not (Test-Path $script:TojiRoot)) { New-Item -ItemType Directory -Path $script:TojiRoot -Force | Out-Null }
    $exe = Join-Path $script:TojiRoot 'toji-timerres.exe'
    if (-not (Test-Path $exe)) {
        $cs = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
class TojiTimerRes {
    [DllImport("ntdll.dll")] static extern int NtSetTimerResolution(uint d, bool s, out uint c);
    [DllImport("ntdll.dll")] static extern int NtQueryTimerResolution(out uint mn, out uint mx, out uint cu);
    static void Main() {
        uint mn, mx, cu;
        NtQueryTimerResolution(out mn, out mx, out cu);
        NtSetTimerResolution(mx, true, out cu);
        Thread.Sleep(Timeout.Infinite);
    }
}
'@
        Add-Type -TypeDefinition $cs -OutputType WindowsApplication -OutputAssembly $exe -ErrorAction Stop
    }
    $action  = New-ScheduledTaskAction -Execute $exe
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $princ   = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
    $set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Hidden -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName 'TOJI Timer Resolution' -Action $action -Trigger $trigger -Principal $princ -Settings $set -Force | Out-Null
    Start-Process -FilePath $exe
    Write-TojiLog 'Timer Resolution helper (exe ligero) instalado y en marcha'
}
function Uninstall-TojiTimerRes {
    Unregister-ScheduledTask -TaskName 'TOJI Timer Resolution' -Confirm:$false -ErrorAction SilentlyContinue
    Get-Process -Name 'toji-timerres' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-TojiLog 'Timer Resolution helper desinstalado'
}

# ==================================== GPU / DRIVERS ===========================
function Get-TojiGpu {
    (Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -and $_.Name -notmatch 'Basic|Remote|Microsoft' } |
        Select-Object -First 1).Name
}
# winget NO instala drivers de GPU (politica). Detectamos y abrimos la web oficial.
function Open-TojiGpuDriver {
    $name = Get-TojiGpu
    $url = $null
    if     ($name -match 'NVIDIA')      { $url = 'https://www.nvidia.com/en-us/software/nvidia-app/' }
    elseif ($name -match 'AMD|Radeon')  { $url = 'https://www.amd.com/en/support/download/drivers.html' }
    elseif ($name -match 'Intel')       { $url = 'https://www.intel.com/content/www/us/en/support/detect.html' }
    if ($url) { Start-Process $url }
    Write-TojiLog "GPU detectada: $name"
    [pscustomobject]@{ Gpu = $name; Url = $url }
}

Write-TojiLog 'toji-core cargado'
