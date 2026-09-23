# TOJI (C# / .NET) - Como compilar

## Requisitos (una vez)
1. Instala el **.NET 8 SDK** (Windows x64): https://dotnet.microsoft.com/download
   - Comprueba en una terminal: `dotnet --version`

## Compilar y ejecutar (desarrollo)
Abre PowerShell en la carpeta `src/TOJI` y:

```powershell
dotnet build
dotnet run
```

## Generar el .exe final (un unico archivo, para distribuir)
```powershell
dotnet publish -c Release
```
El proyecto ya trae la configuracion (PublishSingleFile, win-x64, self-contained). Resultado: **un solo archivo**,
`bin\Release\net8.0-windows\win-x64\publish\TOJI.exe` (~155 MB), sin DLLs ni carpetas al lado.

- **Self-contained** = no hace falta instalar .NET en el PC del usuario.
- Los JSON de `data\` van **incrustados** en el exe. La OTA crea `data\` junto al exe al actualizar y esa copia
  tiene prioridad sobre la incrustada.
- Para descargar, comprime el exe en un .zip (~63 MB). Existe `-p:EnableCompressionInSingleFile=true` (~69 MB), pero
  descomprime en cada arranque: mas lento y mas RAM, tambien en el proceso residente de Timer Resolution.
- Wine no puede abrir estos exe (error 0x8007046C). En Windows funcionan con normalidad.

## Compilar desde Linux/macOS (CI)
WPF solo se ejecuta en Windows, pero se puede compilar en otros sistemas con el SDK oficial de Microsoft:
```bash
dotnet build -p:EnableWindowsTargeting=true
```

## Tests automaticos
Desde la raiz del repo (en Windows usan powershell.exe; en Linux necesitan PowerShell 7, `pwsh`):
```bash
dotnet test tests/TOJI.Tests
```
Cubren: deteccion de fallos de PowerShell, perfiles propios/oficiales, nombres reservados y OTA (firma, hashes, fusion de perfiles).

## Actualizaciones OTA de los JSON
Los tweaks ejecutan comandos como administrador, asi que TOJI solo acepta datos **firmados** con tu clave.
La app solo usa un `data\*.json` de disco si `manifest.sig` es una firma valida (ECDSA P-256) de `manifest.json`
con TU clave, el SHA-256 del fichero coincide con el del manifiesto y su version es mayor que la de los datos
incrustados en el exe. Si no (puesto a mano, editado, otra clave, sin firma...), lo ignora, lo apunta en `toji.log`
y usa la copia incrustada. `tools/OtaSigner` es solo para ti: no va dentro del exe.

1. Una sola vez, en TU PC (nunca en la nube ni en el repo):
   `dotnet run --project tools/OtaSigner -- keygen C:\claves-sothe --source src/TOJI/SignedData.cs`
   - Crea `C:\claves-sothe\ota-private.pem`: guardalo fuera del repo y haz copia (`*.pem` esta en .gitignore).
     Si lo pierdes no podras firmar mas actualizaciones para los exe ya distribuidos.
   - Escribe la clave publica en `SignedData.PublicKey`. Pon tu URL publica en `DataUpdater.DefaultBaseUrl`.
     Recompila y haz commit.
2. Cada actualizacion: edita `src/TOJI/data/*.json` y firma con una version mayor que la anterior:
   `dotnet run --project tools/OtaSigner -- sign src/TOJI/data C:\claves-sothe\ota-private.pem 2`
3. Comprueba antes de publicar: `dotnet run --project tools/OtaSigner -- verify src/TOJI/data src/TOJI/SignedData.cs`
4. Sube a la URL los 4 JSON + `manifest.json` + `manifest.sig` (GitHub Pages de un repo publico, Cloudflare R2...).
   El repo privado NO sirve como origen: los clientes no pueden descargar de el.
5. Haz commit de `src/TOJI/data` (manifest incluido): el siguiente exe lleva esa version incrustada.

Los perfiles que crea el usuario se guardan en `%ProgramData%\TOJI\profiles.user.json`: la OTA y reinstalar TOJI no los tocan.

## Icono de la app
`Assets\icon.ico` sale del logo vectorial (`SotheLogo` en `Themes\TojiTheme.xaml`). Si cambias el logo, regeneralo:
`dotnet run --project tools/IconGen -- src/TOJI/Themes/TojiTheme.xaml src/TOJI/Assets/icon.ico`

## Licencias
- Configura `Licensing/LicenseConfig.cs` (cuenta de Keygen, Worker de pruebas y enlace de compra).
- Sin configurar: en **Debug** se usa el modo DEMO (claves `TOJI-DEMO-PRO`, `TOJI-DEMO-REVOKED`, prueba local de 7 dias);
  en **Release** los ajustes PRO y Guardar perfil quedan bloqueados (nunca gratis por despiste).
- **Paywall inteligente:** cada tweak de `data\tweaks.json` lleva `"isPro"`. Sin licencia, APLICAR aplica lo gratuito
  (privacidad, telemetria, interfaz...) y, si hay algun ajuste PRO nuevo marcado, se para y abre el muro de pago.
  Sin la propiedad el ajuste es gratis. Como `data\` tiene que ir firmado, nadie puede cambiar precios editando el
  JSON; tu si puedes hacerlo con una OTA firmada (marcar o liberar ajustes PRO).
- Revertir, Revertir TODO, Simular, exportar/importar y Reparaciones funcionan siempre sin licencia.

## Notas
- El manifiesto ya pide administrador (UAC) al abrir: no necesitas lanzador .vbs/.bat.
- Si el primer `dotnet build` se queja de `Microsoft.Win32.Registry`, ejecuta:
  `dotnet add package Microsoft.Win32.Registry` y vuelve a compilar.
- Para que SmartScreen no muestre la pantalla azul/roja a otros usuarios, hace falta
  firmar el .exe con un certificado de firma de codigo (de pago). El .exe nativo de .NET
  ya evita la mayoria de falsos positivos de antivirus que daba ps2exe.

## Estructura
```
TOJI.csproj        proyecto
app.manifest       requiere administrador
App.xaml(.cs)      arranque + EULA (carga el tema)
Themes\TojiTheme.xaml  tema "Restriccion Celestial": colores, botones, casillas, combos, scroll...
EulaWindow.*       aviso legal (aceptar/rechazar)
MainWindow.*       interfaz: Optimizaciones, Apps (Winget), Reparaciones, Caracteristicas de Windows
MainWindow.Tabs.cs pestanas Apps (winget + drivers GPU) y Caracteristicas (WMI, con PowerShell de respaldo)
WindowsTools*.cs   winget, caracteristicas opcionales y deteccion de GPU (validan ids antes de usarlos)
TojiDialog.*       dialogos modales con el tema (avisos, confirmaciones, pedir texto)
Ui.cs              CornerRadius adjunto para botones
BorderlessWindow.cs  ventanas sin marco: arrastre, botones propios, maximizar sin tapar la barra de tareas
TimerResolution.cs Timer Resolution 0.5 ms (ntdll) y proceso residente por tarea programada
Assets\icon.ico    icono del .exe y de la barra de tareas (tools/IconGen)
Models.cs          mapeo de los JSON
PowerShellRunner.cs  ejecuta PowerShell oculto y detecta fallos (codigo de salida y stderr)
DataUpdater.cs     OTA de data\*.json firmada (ECDSA) con fusion de perfiles
SignedData.cs      clave publica + verificacion de firma de data\ en cada carga (si no cuadra, JSON incrustado)
Licensing\         huella HWID, cache cifrada (DPAPI), estados y proveedores (Keygen / Demo)
PaywallDialog.*    muro de pago (APLICAR con algun ajuste PRO nuevo, y Guardar perfil)
TojiEngine.cs      motor: aplicar/revertir/backup/codigo/restaurar
data\*.json        tweaks, apps, features, perfiles (editables / OTA)
```

## Pendiente
- Crear la cuenta de Keygen, el Worker (/trial, /webhooks, /transfer) y el producto de Lemon Squeezy; rellenar LicenseConfig.
- Generar las claves OTA (`OtaSigner keygen ... --source`) y publicar los JSON firmados en una URL publica.
- Firma de codigo del .exe (SmartScreen).
