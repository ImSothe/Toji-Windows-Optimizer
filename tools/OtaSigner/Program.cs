using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Firma de los datos (data\*.json) de Sothe Optimizer. Herramienta interna del autor: no va en el .exe.
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
            if ((args.Length == 4 || args.Length == 5) && args[0] == "sign-app")
                return SignApp(args[1], args[2], args[3], args.Length == 5 ? args[4] : "");
            Console.Error.WriteLine("Uso:\n  OtaSigner keygen <carpeta> [--source src/SotheOptimizer/SignedData.cs]\n" +
                                    "  OtaSigner sign <carpeta-data> <ota-private.pem> <version>\n" +
                                    "  OtaSigner verify <carpeta-data> <clave-publica | src/SotheOptimizer/SignedData.cs>\n" +
                                    "  OtaSigner sign-app <SotheOptimizer.exe> <version> <ota-private.pem> [notas]");
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

    // Firma una release de la app: deja release.json (version + SHA-256 del exe) y release.sig junto al .exe.
    // Se suben los tres a la release de GitHub; la app solo instala un binario cuya firma valide con la clave publica.
    private static int SignApp(string exePath, string version, string pemPath, string notes)
    {
        if (!System.Version.TryParse(version.TrimStart('v', 'V'), out _))
            throw new InvalidDataException($"Version invalida: '{version}' (usa algo como 1.1.0).");
        var exeBytes = File.ReadAllBytes(exePath);
        var sha = Convert.ToHexString(SHA256.HashData(exeBytes));
        // size entra en lo firmado: la app descarta una descarga de otro tamano antes de gastar memoria o disco
        var manifest = new { version, file = Path.GetFileName(exePath), sha256 = sha, size = exeBytes.LongLength, notes };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(pemPath));
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        var dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
        File.WriteAllBytes(Path.Combine(dir, "release.json"), bytes);
        File.WriteAllText(Path.Combine(dir, "release.sig"), Convert.ToBase64String(sig));
        Console.WriteLine($"Firmada la release {version}: {Path.GetFileName(exePath)} (SHA-256 {sha[..16]}...)");
        Console.WriteLine("Sube a la release de GitHub: " + Path.GetFileName(exePath) + ", release.json y release.sig");
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
