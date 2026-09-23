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

## Generar el .exe final (limpio, para distribuir)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
El ejecutable queda en:
`bin\Release\net8.0-windows\win-x64\publish\TOJI.exe`

- **Self-contained** = no hace falta instalar .NET en el PC del usuario.
- Junto al exe debe ir la carpeta **`data\`** con los JSON (se copian solos al compilar). Ahi es donde luego haras la actualizacion OTA.

## Compilar desde Linux/macOS (CI)
WPF solo se ejecuta en Windows, pero se puede compilar en otros sistemas con el SDK oficial de Microsoft:
```bash
dotnet build -p:EnableWindowsTargeting=true
```

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
MainWindow.*       interfaz (Optimizaciones + Reparaciones), vista Lista/Cuadricula, toasts
TojiDialog.*       dialogos modales con el tema (avisos, confirmaciones, pedir texto)
Ui.cs              CornerRadius adjunto para botones + barra de titulo oscura (DWM)
Models.cs          mapeo de los JSON
TojiEngine.cs      motor: aplicar/revertir/backup/codigo/restaurar
data\*.json        tweaks, apps, features, perfiles (editables / OTA)
```

## Pendiente para la siguiente iteracion
- Pestanas Apps (Winget) y Caracteristicas de Windows (portar desde la version PowerShell).
- Descarga OTA de los JSON al arrancar (HttpClient -> tu repo de GitHub).
