# Sothe Optimizer — Arquitectura de licencias y freemium

> Estado: **cliente implementado** en `src/SotheOptimizer/Licensing/` + `PaywallDialog` (modo DEMO en Debug). Pendiente: cuenta de Keygen,
> Worker de Cloudflare, producto de Lemon Squeezy y verificación Ed25519 de las respuestas de Keygen (se activa al probar con la cuenta real).
> Precios y funciones verificados en septiembre de 2026; revísalos antes de contratar.

## 1. Requisitos de negocio

| # | Requisito | Cómo se cumple |
|---|-----------|----------------|
| R1 | Licencias permanentes: generar, asignar y revocar en remoto | Keygen crea la licencia al cobrar (webhook) y la suspende/revoca en reembolsos o a mano |
| R2 | Prueba gratuita de X días ligada al PC al instalar | Política *Trial* en Keygen con duración fija y huella única por política |
| R3 | Node-locking: 1 uso activo simultáneo por clave | Política *Permanente* con `maxMachines = 1` y validación por huella (HWID) |
| R4 | "Mirar pero no tocar" | La UI es libre; solo se interceptan **APLICAR** (cuando aplica algo nuevo) y **Guardar perfil** |

## 2. Recomendación

**Keygen (licencias) + Lemon Squeezy (cobro e IVA) + un Cloudflare Worker de puente.**

| Opción | Node-lock por HWID | Prueba ligada al PC sin tarjeta | Revocar en remoto | Validación offline firmada | IVA UE (Merchant of Record) |
|---|---|---|---|---|---|
| **Keygen + Lemon Squeezy** ✅ | Sí (huella + `maxMachines`) | Sí (política Trial + `UNIQUE_PER_POLICY`) | Sí (suspender/revocar + webhooks) | Sí (respuestas y ficheros firmados) | Sí (lo hace Lemon Squeezy) |
| Solo Lemon Squeezy License API | Parcial (límite de activaciones, sin concepto de huella) | No (hay que construirla aparte) | Sí (desactivar clave) | No | Sí |
| Gumroad | No (solo contador de usos) | No | Limitado | No | Sí, pero con comisión claramente mayor |

Por qué esta combinación:

- **Keygen** es una API dedicada a licencias. Tiene justo lo que pide Sothe Optimizer: máquinas con huella, límite de máquinas por licencia, políticas de prueba temporales y la estrategia `UNIQUE_PER_POLICY`. Esta estrategia impide que un mismo PC repita la prueba aunque reinstale Windows, siempre que la huella salga del hardware. El plan **Dev es gratis hasta 100 usuarios activos** y los de pago empiezan en **49 $/mes**. Además, **Keygen CE es autoalojable gratis** si algún día quieres dejar de pagar la nube.
- **Lemon Squeezy** es *Merchant of Record*: cobra, emite facturas y liquida el IVA de cada país de la UE por ti, algo clave vendiendo desde España a consumidores europeos. Cuesta **5 % + 0,50 $ por venta**, con recargos de ~1,5 % en pagos internacionales o por PayPal, y sin cuota mensual. Stripe la compró en 2024 y sigue operando y aceptando altas, aunque su equipo empuja *Stripe Managed Payments* como sucesor. Por eso el diseño deja el cobro **desacoplado**: cambiar de pasarela solo toca el Worker.
- **El Worker** guarda los secretos (token de administrador de Keygen y firma de webhooks) fuera del `.exe`. Un secreto dentro del cliente se extrae en minutos.

## 3. Estados de licencia en la app

```
SinLicencia ──(Empezar prueba)──► Prueba(días restantes) ──(caduca)──► PruebaCaducada
     │                                  │                                   │
     └──────────(Introducir clave válida / compra)──────────────────────────┴──► Licenciada
                                                                                  │
                                                   (reembolso / revocación) ◄─────┘──► Revocada
```

| Estado | Navegar, filtrar, leer, marcar | Simular, exportar/importar código | APLICAR con cambios nuevos | Guardar perfil | Revertir / Revertir TODO / Reparaciones | Apps (Winget) y Características de Windows |
|---|---|---|---|---|---|---|
| SinLicencia / PruebaCaducada / Revocada | ✅ | ✅ | 🔒 muro de pago | 🔒 muro de pago | ✅ **siempre** | ✅ **siempre (decisión: 100 % gratis)** |
| Prueba / Licenciada | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

> **Regla de oro:** revertir nunca se bloquea. Si la prueba caduca con ajustes aplicados, el usuario debe poder dejar Windows como estaba (y APLICAR se permite cuando solo revierte). Tenerle el sistema "secuestrado" generaría reembolsos, disputas y mala fama.

Compartir códigos (exportar/importar) se deja libre a propósito, porque hace de marketing viral: quien recibe un código ve el valor y le salta el muro al aplicarlo.

## 4. Flujos

### 4.1 Compra (licencia permanente)

```mermaid
sequenceDiagram
  participant U as Usuario
  participant App as Sothe Optimizer (C#)
  participant LS as Lemon Squeezy
  participant W as Worker (Cloudflare)
  participant K as Keygen
  U->>App: Pulsa APLICAR sin licencia
  App->>U: Muro de pago (Comprar / Tengo clave / Probar)
  U->>LS: Checkout (email + pago; IVA incluido)
  LS->>W: Webhook order_created (firmado HMAC)
  W->>K: Crea licencia (política Permanente, maxMachines=1)
  W->>U: Email con la clave Sothe Optimizer
  U->>App: Pega la clave
  App->>K: Activa máquina (huella HWID) y valida con scope fingerprint
  K-->>App: Válida + fichero de máquina firmado (Ed25519)
  App->>App: Guarda licencia cifrada (DPAPI) y desbloquea
```

### 4.2 Prueba gratuita ligada al PC

> **Implementado (versión actual):** la prueba es una **clave de 3 meses** de la política de prueba
> (`LicenseConfig.KeygenTrialPolicyId`) que el usuario pega en la app. Se activa en su PC con la propia clave
> (node-locked, 1 máquina) y `UNIQUE_PER_POLICY` impide activar una segunda clave de prueba en el mismo PC.
> El Worker de abajo queda como opción para pruebas de un clic (`LicenseConfig.TrialWorkerUrl`).

1. En el primer arranque, o al pulsar "Empezar prueba de 7 días", la app envía `POST /trial { fingerprint }` al Worker.
2. El Worker, con límite de peticiones por IP, crea una licencia de la política **Trial** (duración 7 días, `maxMachines = 1`, huella `UNIQUE_PER_POLICY`) y activa la máquina.
3. Si esa huella ya consumió una prueba, Keygen rechaza la activación y la app muestra "Ya usaste la prueba en este PC" junto a **Comprar**.
4. La caducidad la decide el servidor (fecha de expiración de Keygen), no el reloj del PC.

### 4.3 Revocación y reembolsos

- Webhook `order_refunded` de Lemon Squeezy → el Worker **suspende** la licencia en Keygen.
- En el siguiente arranque con conexión, la validación devuelve `SUSPENDED` y la app pasa a *Revocada*: se ve y se revierte todo, pero no se puede aplicar.
- También puedes revocar a mano desde el panel de Keygen, por ejemplo si una clave aparece compartida en un foro.

### 4.4 Cambio de PC (transferencia)

Un botón en la app, "Mover mi licencia a este PC", llama a `POST /transfer { key, fingerprint }`. El Worker desactiva la máquina anterior y activa la nueva, con un tope de transferencias (por ejemplo, 2 al mes) contra el uso compartido encubierto. Si se supera, se remite a soporte.

## 5. Integración en C#

### 5.1 Módulos nuevos

```
src/SotheOptimizer/Licensing/
  HardwareId.cs            Huella del PC (ver 5.2)
  LicenseStatus.cs         enum LicenseState + record LicenseStatus(State, DaysLeft, Email, ExpiresAt)
  ILicenseProvider.cs      ActivateAsync / ValidateAsync / StartTrialAsync / TransferAsync
  KeygenLicenseProvider.cs HttpClient -> api.keygen.sh y Worker (verificar la firma Ed25519: pendiente de la cuenta real)
  LicenseStore.cs          %ProgramData%\SotheOptimizer\license.dat cifrado con DPAPI (ProtectedData, LocalMachine)
  LicenseService.cs        Estado actual, caché offline, periodo de gracia, evento StatusChanged
src/SotheOptimizer/PaywallDialog.xaml(.cs)  Modal con la estética de Sothe: clave, Comprar, Probar 7 días
server/worker/            Cloudflare Worker (TypeScript): /trial, /transfer, /webhooks/lemonsqueezy
```

### 5.2 Huella (HWID)

- Componentes: **UUID SMBIOS** (`Win32_ComputerSystemProduct.UUID`) + **serie de placa base** (`Win32_BaseBoard.SerialNumber`), mediante `System.Management`. Sobreviven a reinstalar Windows, que es lo que impide repetir la prueba.
- Algunos fabricantes ponen valores genéricos (`FFFFFFFF-…`, `03000200-0400-…`, "To be filled by O.E.M."). Se detectan y entonces se añade `MachineGuid` (`HKLM\SOFTWARE\Microsoft\Cryptography`).
- Huella = `SHA-256(sal_de_Sothe + componentes normalizados)` en hexadecimal. Nunca se envían los números de serie en claro, lo que ayuda al cumplir el RGPD.

### 5.3 Punto de control en la UI (muro de pago)

Hay un solo método, y solo lo llaman los dos botones de pago:

```csharp
// MainWindow: al inicio de ApplyOrDry(false), tras calcular toApply/toRevert
if (!dryRun && toApply.Count > 0 && !await _license.EnsureCanModifyAsync(this)) return;

// MainWindow.SaveProfile()
if (!await _license.EnsureCanModifyAsync(this)) return;
```

```csharp
public async Task<bool> EnsureCanModifyAsync(Window owner)
{
    if (Status.State is LicenseState.Licensed or LicenseState.Trial) return true;
    var result = PaywallDialog.Show(owner, Status);   // Comprar / Introducir clave / Probar
    if (result.Key != null)   await ActivateAsync(result.Key);
    if (result.StartTrial)    await StartTrialAsync();
    return Status.State is LicenseState.Licensed or LicenseState.Trial;
}
```

En la cabecera, junto al contador, una etiqueta de estado: **PRO** (morado), **PRUEBA · 5 días** (ámbar) o **SIN LICENCIA** (gris, clicable, abre el muro).

### 5.4 Validación, offline y reloj

- **Al arrancar**, en segundo plano, se valida con Keygen (`validate-key` con *scope* de huella). La UI no espera a la red.
- **Sin conexión**, vale el fichero de máquina firmado guardado, con un TTL de 14 días. Pasado ese plazo sin validar se pasa a *SinLicencia* hasta volver a conectar, y revertir sigue libre.
- **Contra el reloj atrasado**, se guarda cifrada la última fecha vista. Si el reloj retrocede de forma sospechosa se exige validar online.
- **Contra suplantar el servidor**, se verifica la firma Ed25519 con la clave pública embebida, así que un servidor falso o un proxy no pueden devolver "válida".

### 5.5 OTA ligada a licencia (valor en servidor)

La descarga OTA de `tweaks.json` nuevos puede exigir la licencia en la petición. Así, los ajustes nuevos y las correcciones llegan solo a quien paga. Es la protección más difícil de piratear, porque el valor vive en el servidor y no en el `.exe`.

## 6. Límites honestos

- **.NET se decompila fácil.** Un cracker puede parchear `EnsureCanModifyAsync` para que devuelva `true`. Ofuscar (Obfuscar/ConfuserEx gratis; .NET Reactor o Eazfuscator de pago) sube el coste, pero ninguna protección local es absoluta. El objetivo es que comprar sea más fácil que piratear, y que lo pirateado se quede sin actualizaciones OTA.
- **La firma de código** (~150–400 €/año) es aparte y necesaria para quitar el aviso de SmartScreen. Sin ella, la tasa de conversión de un producto de pago cae mucho.
- **Licencia del código:** el `LICENSE` MIT de la versión PowerShell permite revender ese código. La versión comercial C# debe ir con licencia propietaria y un EULA de compra, que es una ampliación del aviso actual.

## 7. Costes de arranque

| Servicio | Coste |
|---|---|
| Keygen Cloud Dev | 0 € hasta 100 usuarios activos; después desde 49 $/mes (o CE autoalojado) |
| Lemon Squeezy | 5 % + 0,50 $ por venta (+~1,5 % internacional/PayPal); sin cuota |
| Cloudflare Workers | Capa gratuita suficiente para empezar |
| Email transaccional (p. ej. Resend) | Capa gratuita suficiente para empezar |

## 8. Plan de implementación

1. Crear en Keygen el producto y las políticas **Trial (7 días)** y **Permanente**, y en Lemon Squeezy el producto con un precio único.
2. Worker: `/webhooks/lemonsqueezy` (verificar HMAC → crear licencia → email), `/trial` y `/transfer`.
3. C#: `HardwareId` → `LicenseStore` → `KeygenLicenseProvider` → `LicenseService` → `PaywallDialog` → los dos puntos de control y la etiqueta de estado.
4. Pruebas: compra real en modo test de Lemon Squeezy, reembolso (debe bloquear), prueba repetida en el mismo PC (debe rechazarse), modo offline y reloj atrasado.
