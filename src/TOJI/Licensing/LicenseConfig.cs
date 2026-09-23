using System;

namespace Toji.Licensing
{
    // Rellena estos valores al crear la cuenta de Keygen, el Worker y el producto de Lemon Squeezy
    // (ver docs/ARQUITECTURA-LICENCIAS.md). Mientras esten vacios:
    //  - Debug: se usa el proveedor DEMO (claves TOJI-DEMO-...), marcado como "DEMO" en la interfaz
    //  - Release: APLICAR y Guardar perfil quedan bloqueados (fallo cerrado, nunca gratis por despiste)
    public static class LicenseConfig
    {
        public const string KeygenAccountId = "";   // Keygen > Settings > Account ID
        public const string TrialWorkerUrl = "";    // Worker de Cloudflare con POST /trial
        public const string CheckoutUrl = "";       // enlace de compra de Lemon Squeezy
        public const string SupportEmail = "";      // se muestra en el muro de pago si no esta vacio

        public const int TrialDays = 7;
        public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(14);

        public static bool IsConfigured => KeygenAccountId.Length > 0 && TrialWorkerUrl.Length > 0;
    }
}
