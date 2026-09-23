using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Firma de los datos (data\*.json) de Sothe Celestial Optimizer. Herramienta interna del autor: no va en el .exe.
//   keygen <carpeta> [--source <SignedData.cs>]
//                  crea ota-private.pem (NUNCA al repo) y muestra la clave publica; con --source la escribe en el codigo
//   sign <carpeta-data> <ota-private.pem> <version>
//                  escribe manifest.json (version + SHA-256 de cada JSON) y manifest.sig (firma ECDSA P-256)
//   verify <carpeta-data> <clave-publica-o-SignedData.cs>
//                  comprueba firma y hashes como lo hara la app antes de publicar
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if ((args.Length == 2 || args.Length == 4) && args[0] == "keygen")
                return KeyGen(args[1], args.Length == 4 && args[2] == "--source" ? args[3] : null);
            if (args.Length == 4 && args[0] == "sign") return Sign(args[1], args[2], int.Parse(args[3]));
            if (args.Length == 3 && args[0] == "verify") return Verify(args[1], args[2]);
            Console.Error.WriteLine("Uso:\n  OtaSigner keygen <carpeta> [--source src/TOJI/SignedData.cs]\n" +
                                    "  OtaSigner sign <carpeta-data> <ota-private.pem> <version>\n" +
                                    "  OtaSigner verify <carpeta-data> <clave-publica | src/TOJI/SignedData.cs>");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static readonly Regex PublicKeyConst = new Regex("(public const string PublicKey = \")([^\"]*)(\";)");

    private static int KeyGen(string dir, string sourceFile)
    {
        Directory.CreateDirectory(dir);
        var pem = Path.Combine(dir, "ota-private.pem");
        if (File.Exists(pem)) throw new IOException(pem + " ya existe: no se sobrescribe.");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(pem, ecdsa.ExportPkcs8PrivateKeyPem());
        var pub = ManifestSigner.PublicKeyBase64(ecdsa);
        Console.WriteLine("Clave privada: " + pem + "  (guardala fuera del repo y haz copia de seguridad)");
        Console.WriteLine("Clave publica (SignedData.PublicKey):");
        Console.WriteLine(pub);
        if (sourceFile != null)
        {
            var code = File.ReadAllText(sourceFile);
            if (!PublicKeyConst.IsMatch(code)) throw new InvalidDataException("No se encontro 'public const string PublicKey' en " + sourceFile);
            File.WriteAllText(sourceFile, PublicKeyConst.Replace(code, m => m.Groups[1].Value + pub + m.Groups[3].Value, 1));
            Console.WriteLine("Escrita en " + sourceFile + ": recompila y haz commit.");
        }
        return 0;
    }

    private static int Sign(string dataDir, string pemPath, int version)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(pemPath));
        var files = ManifestSigner.SignDirectory(dataDir, ecdsa, version);
        Console.WriteLine($"Firmada la version {version}: {string.Join(", ", files)}");
        Console.WriteLine("Clave publica de esta firma: " + ManifestSigner.PublicKeyBase64(ecdsa));
        return 0;
    }

    // Misma comprobacion que hace la app (SignedData): firma del manifiesto y SHA-256 de cada fichero
    private static int Verify(string dataDir, string keyOrSource)
    {
        var pub = File.Exists(keyOrSource) ? PublicKeyConst.Match(File.ReadAllText(keyOrSource)).Groups[2].Value : keyOrSource;
        if (string.IsNullOrWhiteSpace(pub)) throw new InvalidDataException("Clave publica vacia: ejecuta antes 'keygen ... --source'.");
        var manifestBytes = File.ReadAllBytes(Path.Combine(dataDir, ManifestSigner.ManifestFile));
        var signature = Convert.FromBase64String(File.ReadAllText(Path.Combine(dataDir, ManifestSigner.SignatureFile)).Trim());
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub), out _);
        if (!ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256))
        {
            Console.Error.WriteLine("FIRMA NO VALIDA: la app ignoraria estos datos.");
            return 1;
        }
        using var doc = JsonDocument.Parse(manifestBytes);
        int bad = 0;
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateObject())
        {
            var path = Path.Combine(dataDir, f.Name);
            bool ok = File.Exists(path) && string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), f.Value.GetString(), StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  {(ok ? "OK      " : "CAMBIADO")} {f.Name}");
            if (!ok) bad++;
        }
        Console.WriteLine(bad == 0 ? $"Version {doc.RootElement.GetProperty("version").GetInt32()} correcta: lista para publicar."
                                   : $"{bad} fichero(s) no coinciden: vuelve a firmar.");
        return bad == 0 ? 0 : 1;
    }
}
