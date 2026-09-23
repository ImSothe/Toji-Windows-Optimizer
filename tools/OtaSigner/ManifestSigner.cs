using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

// Firma de los datos de la app. Solo la usan OtaSigner y los tests: no forma parte del .exe del cliente,
// que unicamente lleva la clave PUBLICA y la verificacion (src/TOJI/SignedData.cs).
internal static class ManifestSigner
{
    public static readonly string[] Files = { "tweaks.json", "apps.json", "features.json", "profiles.json" };
    public const string ManifestFile = "manifest.json";
    public const string SignatureFile = "manifest.sig";

    // Escribe en dataDir manifest.json (version + SHA-256 de cada JSON presente) y manifest.sig (firma ECDSA P-256
    // en base64 de manifest.json). Devuelve los ficheros firmados.
    public static string[] SignDirectory(string dataDir, ECDsa privateKey, int version)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "La version debe ser 1 o mayor.");
        var present = Files.Where(f => File.Exists(Path.Combine(dataDir, f))).ToArray();
        if (present.Length == 0) throw new FileNotFoundException("No hay JSON que firmar en " + dataDir);
        var manifest = new
        {
            version,
            files = present.ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dataDir, f)))))
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256);
        File.WriteAllBytes(Path.Combine(dataDir, ManifestFile), bytes);
        File.WriteAllText(Path.Combine(dataDir, SignatureFile), Convert.ToBase64String(signature));
        return present;
    }

    public static string PublicKeyBase64(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
}
