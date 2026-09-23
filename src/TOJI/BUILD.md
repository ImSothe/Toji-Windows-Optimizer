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
1. Una sola vez: `dotnet run --project tools/OtaSigner -- keygen C:\claves-toji`
   - Guarda `ota-private.pem` FUERA del repo (y haz copia). Nunca se sube (`*.pem` esta en .gitignore).
   - Pega la clave publica que imprime en `DataUpdater.DefaultPublicKey` y tu URL publica en `DataUpdater.DefaultBaseUrl`.
2. Cada actualizacion: edita los JSON, sube la version y firma:
   `dotnet run --project tools/OtaSigner -- sign src/TOJI/data C:\claves-toji\ota-private.pem 2`
3. Publica en la URL: `manifest.json`, `manifest.sig` y los JSON (GitHub Pages de un repo publico, Cloudflare R2...).
   El repo privado NO sirve como origen: los clientes no pueden descargar de el.

Los perfiles que crea el usuario se guardan en `%ProgramData%\TOJI\profiles.user.json`: la OTA y reinstalar TOJI no los tocan.

## Icono de la app
`Assets\icon.ico` sale del logo vectorial (`SotheLogo` en `Themes\TojiTheme.xaml`). Si cambias el logo, regeneralo:
`dotnet run --project tools/IconGen -- src/TOJI/Themes/TojiTheme.xaml src/TOJI/Assets/icon.ico`

## Licencias
- Configura `Licensing/LicenseConfig.cs` (cuenta de Keygen, Worker de pruebas y enlace de compra).
- Sin configurar: en **Debug** se usa el modo DEMO (claves `TOJI-DEMO-PRO`, `TOJI-DEMO-REVOKED`, prueba local de 7 dias);
  en **Release** APLICAR y Guardar perfil quedan bloqueados (nunca gratis por despiste).
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
Licensing\         huella HWID, cache cifrada (DPAPI), estados y proveedores (Keygen / Demo)
PaywallDialog.*    muro de pago (solo en APLICAR con cambios nuevos y Guardar perfil)
TojiEngine.cs      motor: aplicar/revertir/backup/codigo/restaurar
data\*.json        tweaks, apps, features, perfiles (editables / OTA)
```

## Pendiente
- Crear la cuenta de Keygen, el Worker (/trial, /webhooks, /transfer) y el producto de Lemon Squeezy; rellenar LicenseConfig.
- Generar las claves OTA (tools/OtaSigner) y publicar los JSON firmados en una URL publica.
- Firma de codigo del .exe (SmartScreen).
