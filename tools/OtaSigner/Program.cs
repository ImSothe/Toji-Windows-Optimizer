using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

// Firma de las actualizaciones OTA de TOJI.
//   keygen <carpeta>            crea ota-private.pem (NO subir nunca al repo) e imprime la clave publica
//   sign <carpeta-data> <ota-private.pem> <version>
//                               escribe manifest.json (version + SHA-256 de cada JSON) y manifest.sig
// Publica en tu servidor: manifest.json, manifest.sig y los JSON.
internal static class Program
{
    private static readonly string[] Files = { "tweaks.json", "apps.json", "features.json", "profiles.json" };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "keygen") return KeyGen(args[1]);
            if (args.Length == 4 && args[0] == "sign") return Sign(args[1], args[2], int.Parse(args[3]));
            Console.Error.WriteLine("Uso:\n  OtaSigner keygen <carpeta>\n  OtaSigner sign <carpeta-data> <ota-private.pem> <version>");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static int KeyGen(string dir)
    {
        Directory.CreateDirectory(dir);
        var pem = Path.Combine(dir, "ota-private.pem");
        if (File.Exists(pem)) throw new IOException(pem + " ya existe: no se sobrescribe.");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(pem, ecdsa.ExportPkcs8PrivateKeyPem());
        Console.WriteLine("Clave privada: " + pem + "  (guardala fuera del repo y haz copia de seguridad)");
        Console.WriteLine("Clave publica para DataUpdater.DefaultPublicKey:");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    private static int Sign(string dataDir, string pemPath, int version)
    {
        var present = Files.Where(f => File.Exists(Path.Combine(dataDir, f))).ToList();
        if (present.Count == 0) throw new FileNotFoundException("No hay JSON que firmar en " + dataDir);
        var manifest = new
        {
            version,
            files = present.ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dataDir, f)))))
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(pemPath));
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        File.WriteAllBytes(Path.Combine(dataDir, "manifest.json"), bytes);
        File.WriteAllText(Path.Combine(dataDir, "manifest.sig"), Convert.ToBase64String(sig));
        Console.WriteLine($"Firmada la version {version}: {string.Join(", ", present)}");
        return 0;
    }
}
