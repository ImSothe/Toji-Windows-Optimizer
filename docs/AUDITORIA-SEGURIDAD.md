# Auditoría de seguridad y calidad — Sothe Optimizer 1.1.0

Fecha: 2026-10-10 · Alcance: `src/`, `tools/OtaSigner`, `tests/` (commit base `432c631`) · Método: revisión manual del código (SAST) y pruebas automatizadas.

## Estado de compilación y pruebas

| Comprobación | Resultado |
|---|---|
| `dotnet build -c Release` (SDK 10, destino net8.0) | **0 errores, 0 advertencias** |
| `dotnet test` | **212 correctas, 0 fallos, 2 omitidas** (las 2 omitidas son contra `api.keygen.sh` y solo corren con `SOTHE_KEYGEN_LIVE=1`) |
| Antes de la auditoría | 158 correctas, 2 fallos (los tests asumían PowerShell 7 `pwsh`, no instalado) |
| `dotnet publish -c Release` | un solo `SotheOptimizer.exe` (~155 MB, runtime incluido) que arranca y responde |
| Secretos en el repo | **Ninguno** (sin claves, tokens ni `.pem` rastreados). Los IDs de Keygen son públicos por diseño |

## Hallazgos y correcciones

Severidades pensadas para una app que corre **siempre elevada**.

### ALTA

| # | Hallazgo | Corrección |
|---|---|---|
| A1 | **Escalada de privilegios por la tarea programada de Timer Resolution.** Se registraba con `RunLevel Highest` apuntando al `.exe` donde estuviera (p. ej. Descargas, carpeta escribible por el usuario): cualquier programa sin privilegios podía sustituirlo y ejecutarse como administrador en el siguiente inicio de sesión. | `TimerResolution.PrepareProtectedCopy`: el `.exe` se copia a `Archivos de programa\SotheOptimizer` (ACL de solo administradores) y la tarea apunta ahí. Al desinstalar se borra la copia. |
| A2 | **`%ProgramData%\SotheOptimizer` escribible por usuarios estándar.** La app (elevada) lee y **ejecuta** desde ahí: el campo `revert` de los backups (comando de PowerShell), la actualización preparada y `apply-update.cmd`. ProgramData deja a cualquier usuario crear ficheros/carpetas y ser dueño de ellos: un backup plantado con un `revert` malicioso se ejecutaba como administrador al revertir; pre-crear `update\apply-update.cmd` permitía una carrera (TOCTOU) antes de ejecutarlo. | `SecureStorage.Harden` al arrancar: si la carpeta no pertenece a un administrador se aparta y se recrea; ACL protegida (SYSTEM/Administradores control total, Usuarios solo lectura); se elimina todo lo que no sea de un administrador. Sin carpeta de confianza no se instalan actualizaciones. |
| A3 | **Caché de licencia no autenticada** (`license.dat`). DPAPI con ámbito `LocalMachine` y entropía pública: un usuario local puede fabricar un fichero válido (la huella se calcula con datos públicos y sal pública) con `LastValidatedUtc` en el futuro: la comprobación de gracia offline (`now - LastValidated > 14 días`) nunca saltaba. | Parche parcial: una validación con fecha futura obliga a volver a validar (`ValidationRequired`). **Mejora recomendada (pendiente):** que la caché guarde la respuesta de Keygen **firmada** (cabecera `Keygen-Signature`, Ed25519) y se verifique offline con la clave pública de la cuenta, y servir los ajustes PRO desde el servidor sólo a licencias válidas. Con la lógica en el cliente, un atacante con tiempo y un descompilador siempre puede parchearla. |

### MEDIA

| # | Hallazgo | Corrección |
|---|---|---|
| M1 | **Secuestro de ejecutables por nombre suelto** (`cmd.exe`, `powershell.exe`, `winget`). `CreateProcess` busca primero en la carpeta del propio `.exe`: un `cmd.exe` junto al ejecutable (Descargas) se lanzaba como administrador. | `SystemPaths`: rutas absolutas de System32 y del alias de winget del usuario. Aplicado en `PowerShellRunner`, `AppUpdater`, `RunPowerShellVisible` y `WindowsToolsNative`. |
| M2 | **Auto-updater: descarga entera en memoria sin tope** (`GetByteArrayAsync` de ~155 MB), sin comprobar HTTPS ni host de las URLs que vienen en el JSON de GitHub. | Streaming a disco con SHA‑256 incremental, tope de tamaño (declarado en el manifiesto firmado o 400 MB), `Content-Length` comprobado, solo `https` y hosts de GitHub (`IsAllowedUrl`), repositorio validado con regex. `OtaSigner sign-app` ahora firma también `size`. |
| M3 | **Auto-updater: manejo de errores incompleto.** Una firma con Base64 roto lanzaba `FormatException` fuera de los `catch` esperados; URLs inválidas, `UnauthorizedAccessException`... | Lista única de errores esperables; un fallo de actualización nunca tumba la app. |
| M4 | **Script de reemplazo frágil.** Sin tiempo máximo (bucle infinito si el proceso no muere), espera por subcadena del PID (`find "12"` coincide con `1234`), sin copia de seguridad (si `move` falla o se corta entre volúmenes, la app se queda sin `.exe`), `%` en rutas se expandía. | `BuildSwapScript`: espera por nombre de imagen con máximo ~90 s, copia `.old` antes de mover, restaura si falla y deja `update.log`, `%` duplicado. |
| M5 | **Extracción del single-file a `%TEMP%\.net`** (carpeta del usuario) por un proceso elevado: un proceso del mismo usuario puede plantar bibliotecas antes del arranque. UAC no es una frontera de seguridad en Windows, pero el riesgo existe. | **Sin parche de código (limitación conocida).** Recomendación: distribuir con instalador a `Archivos de programa`, o publicar sin `IncludeNativeLibrariesForSelfExtract` en una carpeta protegida. |

### BAJA / INFORMATIVA

| # | Hallazgo | Estado |
|---|---|---|
| B1 | `NtSetTimerResolution` marshalaba `bool` como BOOL de 4 bytes (el parámetro es BOOLEAN de 1) y el NTSTATUS se ignoraba. | **Corregido** (`[MarshalAs(U1)]`, se lanza si el estado ≠ 0). |
| B2 | Timer Resolution dependía de que Windows liberara la petición al morir el proceso. | **Corregido**: `Release()` explícito, evento de parada con nombre (`Global\SotheOptimizerTimerResStop`) y cierre ordenado antes de matar el proceso. |
| B3 | Nombres de servicio y de paquete Appx de `tweaks.json` y el título de `RunPowerShellVisible` acababan dentro de comandos de PowerShell sin validar. Los datos están firmados, pero es defensa en profundidad. | **Corregido** (`IsValidServiceName`, `IsValidAppxName`, `SafeTitle`). |
| B4 | Si WMI falla, la huella es `UNIDENTIFIED` para todos esos equipos (colisión de licencias). | Pendiente: rechazar la activación con huella no identificada. |
| B5 | `-ExecutionPolicy Bypass` en todos los comandos de PowerShell. | Inherente al diseño (comandos propios y firmados); documentado. |
| B6 | Una sola clave de firma para la app y para los datos OTA. | Recomendado separar claves y planificar rotación (clave pública de reserva). |
| B7 | El `.exe` no lleva firma Authenticode: SmartScreen avisa. | Pendiente (certificado de pago). La integridad de las actualizaciones no depende de ella. |
| B8 | Cuando el binario se descompila (IL), el código es legible. | Limitación de .NET. Ofuscar reduce, no elimina; ver informe. |

## Verificado como correcto

- **Firma del updater y de los datos OTA:** ECDSA P‑256 + SHA‑256, comparación del manifiesto firmado, el `.exe` se busca por el nombre **firmado** (no por el de GitHub), el nombre del fichero nunca se usa como ruta, `AllowedFiles` limita la OTA.
- **Version rollback:** solo se instala `version > actual`.
- **Elevación:** el manifiesto pide `requireAdministrator`; si el usuario cancela UAC, Windows no arranca el proceso y no hay estado a medias. Un `asInvoker` con elevación bajo demanda sería posible, pero el producto necesita administrador para casi todo (HKLM, servicios, características).
- **Inyección:** los ids de winget y de Características se validan con regex y se pasan con `ArgumentList`; el escapado del script de la tarea duplica las comillas simples; `PowerShellRunner` detecta fallos reales (código de salida y stderr) y tiene tiempo máximo.
- **P/Invoke / handles:** solo `ntdll` (2 funciones, sin buffers de tamaño variable ni memoria no gestionada); `Process`, `HttpClient`, `FileStream`, `Mutex` y `EventWaitHandle` se liberan con `using`.
- **Registro:** hives limitados a HKLM/HKCU/HKCR/HKU y las rutas salen de datos firmados.

## Cómo reproducirlo

```powershell
dotnet build SotheOptimizer.sln -c Release
dotnet test tests/SotheOptimizer.Tests -c Release
dotnet publish src/SotheOptimizer -c Release -o ..\release
dotnet run --project tools/OtaSigner -- sign-app <exe> <version> C:\claves-sothe\ota-private.pem "<notas>"
```
