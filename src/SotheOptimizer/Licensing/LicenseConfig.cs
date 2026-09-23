using System;

namespace Sothe.Licensing
{
    // Configuracion de licencias (ver docs/ARQUITECTURA-LICENCIAS.md). Los IDs de Keygen NO son secretos: identifican
    // la cuenta, el producto y la politica, pero no dan acceso de administrador (ese token nunca va en la app).
    //  - Con KeygenAccountId y KeygenProductId rellenos se usa Keygen (validacion con huella, 1 PC por clave).
    //  - Sin ellos: en Debug el proveedor DEMO (claves SOTHE-DEMO-...); en Release los ajustes PRO quedan bloqueados.
    public static class LicenseConfig
    {
        public const string KeygenAccountId = "00000000-0000-0000-0000-000000000000";
        public const string KeygenProductId = "00000000-0000-0000-0000-000000000000";
        // Politica de las claves de prueba de 3 meses (node-locked: una sola maquina por clave). Las licencias de esta
        // politica se muestran como PRUEBA con los dias restantes; las de cualquier otra politica del producto, como PRO
        public const string KeygenTrialPolicyId = "00000000-0000-0000-0000-000000000000";
        public const int KeyTrialMonths = 3;

        public const string TrialWorkerUrl = "";    // opcional: Worker con POST /trial para pruebas de un clic (vacio = solo por clave)
        public const string CheckoutUrl = "";       // enlace de compra (Lemon Squeezy...)
        public const string SupportEmail = "";      // se muestra en el muro de pago si no esta vacio

        public const int TrialDays = 7;             // duracion de las pruebas de un clic del Worker (y de la demo)
        public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(14);

        public static bool IsConfigured => KeygenAccountId.Length > 0 && KeygenProductId.Length > 0;
    }
}
