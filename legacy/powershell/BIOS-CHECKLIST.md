# BIOS / UEFI — checklist de baja latencia

El firmware es anterior al sistema operativo, así que **ningún optimizador de Windows lo toca**. Esto se hace a mano entrando en la BIOS (normalmente `Supr` o `F2` al arrancar). Los nombres varían por placa (ASUS/MSI/Gigabyte/ASRock).

> Anota los valores originales antes de cambiarlos. Cambia de uno en uno y prueba estabilidad.

## Alto impacto (haz esto sí o sí)

- **XMP / EXPO / DOCP: ON.** Sin esto tu RAM corre a la frecuencia base (2133–3200) en vez de la que compraste. Es la mejora gratis más grande que existe.
- **Resizable BAR (ReBAR) / Smart Access Memory: ON.** Sobre todo en GPUs RTX 30/40 y RX 6000+. Suele dar FPS gratis.
- **Modo de arranque: solo UEFI**, CSM off (rendimiento y arranque más limpio).

## Latencia / consistencia (para competir)

- **C-States (C1E, Package C-State): OFF** o limitados. Mantiene la CPU despierta → menos latencia al despertar hilos. Sube algo el consumo/temperatura.
- **Global C-State Control (AMD): Disabled** por el mismo motivo.
- **Power Supply Idle Control (AMD): Typical Current Idle** (evita cuelgues en reposo con C-States tocados).
- **CPU Power Management / EIST-SpeedStep:** para máxima consistencia, off; para equilibrio, déjalo y usa el plan de energía de Windows.
- **HPET: déjalo en Auto.** El mito de "HPET off = más FPS" es muy dependiente del sistema; mide antes de tocar.

## Overclock / tuning (opcional, con cabeza)

- **PBO (AMD) / MCE (Intel):** rendimiento extra si tu refrigeración aguanta. Vigila temperaturas.
- **Curve Optimizer (AMD):** undervolt por curva → más boost sostenido y menos calor. Requiere pruebas de estabilidad.
- **Fan curve:** una curva agresiva evita throttling térmico en partidas largas (throttling = frametimes irregulares = lo que notas como "va lento").

## Seguridad (relacionado con tus tweaks de Windows)

- **VT-x / SVM / Virtualización:** si desactivas VBS/Hyper-V en Windows y no usas VMs/WSL2, puedes dejarla off. Si usas máquinas virtuales, déjala on.
- **Secure Boot:** algunos anti-cheat (Valorant/Vanguard, Faceit) **exigen Secure Boot ON**. Si compites, no lo desactives.
- **TPM 2.0:** requerido por Windows 11 y por varios anti-cheat. No lo toques si juegas competitivo.

## Orden de trabajo sugerido

1. XMP/EXPO on → prueba estabilidad (memtest o un rato de juego).
2. ReBAR on → mide FPS antes/después.
3. C-States off → nota la consistencia (1% lows), no tanto el FPS medio.
4. Guarda un perfil de BIOS (la mayoría de placas permiten guardar/exportar perfiles) por si algo falla.

---
*Regla de oro: cambia una cosa, prueba, y si algo peta entra en BIOS y carga tu perfil o los "Optimized Defaults". La BIOS no se "brickea" por estos ajustes; siempre puedes resetear con el jumper Clear CMOS o quitando la pila.*
