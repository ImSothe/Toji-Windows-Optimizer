# Sothe Optimizer (C# / .NET) - Como compilar

## Requisitos (una vez)
1. Instala el **.NET 8 SDK** (Windows x64): https://dotnet.microsoft.com/download
   - Comprueba en una terminal: `dotnet --version`

## Compilar y ejecutar (desarrollo)
Abre PowerShell en la carpeta `src/SotheOptimizer` y:

```powershell
dotnet build
dotnet run
```

## Generar el .exe final (un unico archivo, para distribuir)
```powershell
dotnet publish -c Release
```
El proyecto ya trae la configuracion (PublishSingleFile, win-x64, self-contained). Resultado: **un solo archivo**,
`bin\Release\net8.0-windows\win-x64\publish\SotheOptimizer.exe` (~155 MB), sin DLLs ni carpetas al lado.

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
dotnet test tests/SotheOptimizer.Tests
```
Cubren: deteccion de fallos de PowerShell, perfiles propios/oficiales, nombres reservados y OTA (firma, hashes, fusion de perfiles).

## Actualizaciones OTA de los JSON
Los tweaks ejecutan comandos como administrador, asi que la app solo acepta datos **firmados** con tu clave.
La app solo usa un `data\*.json` de disco si `manifest.sig` es una firma valida (ECDSA P-256) de `manifest.json`
con TU clave, el SHA-256 del fichero coincide con el del manifiesto y su version es mayor que la de los datos
incrustados en el exe. Si no (puesto a mano, editado, otra clave, sin firma...), lo ignora, lo apunta en `sothe.log`
y usa la copia incrustada. `tools/OtaSigner` es solo para ti: no va dentro del exe.

1. Una sola vez, en TU PC (nunca en la nube ni en el repo):
   `dotnet run --project tools/OtaSigner -- keygen C:\claves-sothe --source src/SotheOptimizer/SignedData.cs`
   - Crea `C:\claves-sothe\ota-private.pem`: guardalo fuera del repo y haz copia (`*.pem` esta en .gitignore).
     Si lo pierdes no podras firmar mas actualizaciones para los exe ya distribuidos.
   - Escribe la clave publica en `SignedData.PublicKey`. Pon tu URL publica en `DataUpdater.DefaultBaseUrl`.
     Recompila y haz commit.
2. Cada actualizacion: edita `src/SotheOptimizer/data/*.json` y firma con una version mayor que la anterior:
   `dotnet run --project tools/OtaSigner -- sign src/SotheOptimizer/data C:\claves-sothe\ota-private.pem 2`
3. Comprueba antes de publicar: `dotnet run --project tools/OtaSigner -- verify src/SotheOptimizer/data src/SotheOptimizer/SignedData.cs`
4. Sube a la URL los 4 JSON + `manifest.json` + `manifest.sig` (GitHub Pages de un repo publico, Cloudflare R2...).
   El repo privado NO sirve como origen: los clientes no pueden descargar de el.
5. Haz commit de `src/SotheOptimizer/data` (manifest incluido): el siguiente exe lleva esa version incrustada.

Los perfiles que crea el usuario se guardan en `%ProgramData%\SotheOptimizer\profiles.user.json`: la OTA y reinstalar la app no los tocan.

## Auto-actualizacion del .exe (opcional)
Actualiza la app entera, no solo los JSON. **Regla de oro: la app NUNCA ejecuta un binario descargado sin verificar
antes la firma del autor y el SHA-256.** Un GitHub secuestrado o un ataque de red no pueden colar un binario que la
app llegue a ejecutar: sin la clave privada (que solo tienes tu) no se puede firmar una release.

### Donde alojar las descargas (repo privado)
El codigo puede seguir en un repo privado. Las **releases** (el .exe y las firmas) tienen que estar en un sitio
**publico** para que la app las descargue sin pedir credenciales. Lo mas sencillo y gratis: un **segundo repo publico
solo para descargas**, p. ej. `ImSothe/sothe-optimizer-releases` (vacio de codigo, solo releases). GitHub lo hospeda
gratis y la app usa su API de releases tal cual. (Alternativas validas: GitHub Pages o Cloudflare R2; con el repo de
codigo 100% privado, la app NO puede descargar de el.)

### Pasos por version
1. Una sola vez: en `src/SotheOptimizer/AppUpdater.cs`, `GitHubRepo = "ImSothe/sothe-optimizer-releases"` (tu repo
   PUBLICO de descargas). Reutiliza la misma clave de firma que la OTA (`SignedData.PublicKey`); si aun no la tienes,
   generala con `OtaSigner keygen ... --source`.
2. Sube el `<Version>` en `SotheOptimizer.csproj` (p. ej. a `1.1.0`), o la app no vera la nueva como "mas nueva".
3. Compila y firma de un tiron con el script (deja los 3 ficheros en la carpeta `actualizacion\`):
   `.\construir_actualizacion.ps1 -Version 1.1.0 -Pem C:\claves-sothe\ota-private.pem -Notes "Novedades"`
4. Crea una **release** en el repo publico con la etiqueta `v1.1.0` y sube los 3 assets de `actualizacion\`:
   `SotheOptimizer.exe`, `release.json` y `release.sig`.

Como funciona en el cliente: **con solo abrir la app**, al arrancar comprueba en silencio la ultima release; si la
firma valida y hay version mayor, descarga el .exe y comprueba su SHA-256 contra el manifiesto firmado. Si el usuario
aun no ha tocado el sistema, **se instala y reinicia sola** (un solo reinicio, sin clics): un script temporal espera a
que la app se cierre, reemplaza el .exe y la relanza ya actualizada. Si en esos segundos el usuario ya estaba
aplicando o revirtiendo algo, la instalacion se pospone al siguiente arranque para no cortar un cambio a medias (avisa
con un toast). Sin `GitHubRepo`, no hace ninguna llamada de red.

## Icono de la app
`Assets\icon.ico` sale del logo vectorial (`SotheLogo` en `Themes\SotheTheme.xaml`). Si cambias el logo, regeneralo:
`dotnet run --project tools/IconGen -- src/SotheOptimizer/Themes/SotheTheme.xaml src/SotheOptimizer/Assets/icon.ico`

## Licencia
Codigo publico bajo licencia PolyForm Noncommercial 1.0.0 (ver `LICENSE`): uso libre para cualquier fin no comercial. No hay licencias de pago, claves ni muro de pago: todos los ajustes se aplican
sin restricciones, y revertir es siempre posible.

## Notas
- **Ajustes "PROHIBIDO":** los 8 tweaks de alto riesgo (desactivar Defender, Firewall, UAC, mitigaciones
  Spectre/Meltdown, Hypervisor, HPET, bcdedit de temporizadores, idle de CPU) siguen en el motor y en
  `data\tweaks.json`. En la interfaz estan ocultos hasta activar el switch **"Modo avanzado"** (con confirmacion),
  como pide el EULA. No estan borrados: el filtro `hiddenByAdv` en `MainWindow` solo los oculta con el modo apagado.
- El manifiesto ya pide administrador (UAC) al abrir: no necesitas lanzador .vbs/.bat.
- Si el primer `dotnet build` se queja de `Microsoft.Win32.Registry`, ejecuta:
  `dotnet add package Microsoft.Win32.Registry` y vuelve a compilar.
- Para que SmartScreen no muestre la pantalla azul/roja a otros usuarios, hace falta
  firmar el .exe con un certificado de firma de codigo (de pago). El .exe nativo de .NET
  ya evita la mayoria de falsos positivos de antivirus que daba ps2exe.

## Estructura
```
SotheOptimizer.csproj        proyecto
app.manifest       requiere administrador
App.xaml(.cs)      arranque + EULA (carga el tema)
Themes\SotheTheme.xaml  tema oscuro con acentos neon: colores, botones, casillas, combos, scroll...
EulaWindow.*       aviso legal (aceptar/rechazar)
MainWindow.*       interfaz: Optimizaciones, Apps (Winget), Reparaciones, Caracteristicas de Windows
MainWindow.Tabs.cs pestanas Apps (winget + drivers GPU) y Caracteristicas (WMI, con PowerShell de respaldo)
WindowsTools*.cs   winget, caracteristicas opcionales y deteccion de GPU (validan ids antes de usarlos)
SotheDialog.*       dialogos modales con el tema (avisos, confirmaciones, pedir texto)
Ui.cs              CornerRadius adjunto para botones
BorderlessWindow.cs  ventanas sin marco: arrastre, botones propios, maximizar sin tapar la barra de tareas
TimerResolution.cs Timer Resolution 0.5 ms (ntdll) y proceso residente por tarea programada
Assets\icon.ico    icono del .exe y de la barra de tareas (tools/IconGen)
Models.cs          mapeo de los JSON
PowerShellRunner.cs  ejecuta PowerShell oculto y detecta fallos (codigo de salida y stderr)
DataUpdater.cs     OTA de data\*.json firmada (ECDSA) con fusion de perfiles
SignedData.cs      clave publica + verificacion de firma de data\ en cada carga (si no cuadra, JSON incrustado)
AppUpdater.cs      auto-actualizacion del .exe: descarga solo firmada (ECDSA)+SHA-256, se instala al reiniciar
SotheEngine.cs      motor: aplicar/revertir/backup/codigo/restaurar
data\*.json        tweaks, apps, features, perfiles (editables / OTA)
```

## Pendiente
- Generar las claves OTA (`OtaSigner keygen ... --source`) y publicar los JSON firmados en una URL publica, si se quiere activar la OTA de datos.
- Firma de codigo del .exe (SmartScreen).
