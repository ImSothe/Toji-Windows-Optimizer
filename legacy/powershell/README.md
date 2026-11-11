<div align="center">

# 🟣 TOJI

**Suite de optimización de Windows para usuarios expertos.**
Sin ruedines: llega hasta donde otras no te dejan — y todo es reversible.

</div>

---

## ¿Qué es?

TOJI deja tu PC con Windows más rápido, limpio y a tu medida. A diferencia de otros optimizadores, **no te esconde las opciones peligrosas**: te deja tocar hasta "lo prohibido"… pero guardando el valor anterior de cada cambio, con punto de restauración y botón de pánico para volver atrás.

Pensada para gaming competitivo y para labs de ciberseguridad.

## Características

- **Optimizaciones** — 67 ajustes clasificados por peligro (safe → extreme), agrupados por categoría. Perfiles listos (Competitivo / Ciberseguridad / Limpio) y perfiles propios.
- **Perfiles con código** — exporta tu configuración como código y clónala en otro PC en dos clics.
- **Reversible de verdad** — cada tweak guarda el estado previo. Revert individual, por perfil, o "revertir todo".
- **Instalador de apps (Winget)** — Steam, Epic, Discord, navegadores, herramientas de dev y de ciber, de un clic.
- **Reparaciones** — SFC, DISM, reset de red, reparar Windows Update, CHKDSK.
- **Características de Windows** — activa/desactiva WSL, Hyper-V, Sandbox, etc.

## Instalación

1. Descarga o clona el repo.
2. Ejecuta **`TOJI.vbs`** (o compila un `.exe` con `build-exe.ps1`).
3. Acepta el permiso de administrador.

> Guía de usuario completa: **[GUIA.md](GUIA.md)**

## Aviso

TOJI es una herramienta para **usuarios expertos**. Algunos ajustes desactivan protecciones de seguridad o pueden afectar a la estabilidad. **Crea un punto de restauración antes de usarla.** El autor no se hace responsable del uso que se le dé. Los anti-cheat estrictos (Vanguard/Valorant) pueden reaccionar a ciertos ajustes.

## Cómo funciona

Un motor en PowerShell (`toji-core.ps1`) aplica operaciones definidas en JSON (`tweaks.json`, `apps.json`, `features.json`, `profiles.json`) y una interfaz WPF (`TOJI-GUI.ps1`) las presenta. Añadir un tweak es editar el JSON — no hace falta tocar código.

## Licencia

PolyForm Noncommercial 1.0.0: uso libre para cualquier fin no comercial. Ver [LICENSE](LICENSE).
