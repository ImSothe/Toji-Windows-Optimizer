using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Toji.Licensing
{
    // Huella del PC: SHA-256(sal + UUID de BIOS + serie de placa base). Ambos sobreviven a reinstalar
    // Windows (asi la prueba no se repite). MachineGuid solo completa si faltan. Nunca se envian en claro.
    public static class HardwareId
    {
        private const string Salt = "TOJI-Celestial-HWID-v1";

        // Valores de relleno que ponen algunos fabricantes: no identifican nada
        private static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.Ordinal)
        {
            "NONE", "DEFAULT STRING", "TO BE FILLED BY O.E.M.", "TO BE FILLED BY OEM", "SYSTEM SERIAL NUMBER",
            "BASE BOARD SERIAL NUMBER", "SERIAL NUMBER", "NOT APPLICABLE", "N/A", "INVALID", "O.E.M.", "OEM",
            "123456789", "0123456789", "00000000-0000-0000-0000-000000000000",
            "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "03000200-0400-0500-0006-000700080009"
        };

        public static string Normalize(string value) => (value ?? "").Trim().ToUpperInvariant();

        public static bool IsUsable(string value)
        {
            var n = Normalize(value);
            return n.Length >= 4 && !Generic.Contains(n) && n.Where(char.IsLetterOrDigit).Distinct().Count() > 2;
        }

        public static string Compute(string biosUuid, string boardSerial, string machineGuid)
        {
            var parts = new List<string>();
            if (IsUsable(biosUuid)) parts.Add("uuid:" + Normalize(biosUuid));
            if (IsUsable(boardSerial)) parts.Add("board:" + Normalize(boardSerial));
            if (parts.Count < 2 && IsUsable(machineGuid)) parts.Add("guid:" + Normalize(machineGuid));
            if (parts.Count == 0) throw new InvalidOperationException("No se pudo identificar este equipo.");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + "|" + string.Join("|", parts))));
        }
    }
}
