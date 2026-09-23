using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sothe
{
    public class OtaManifest
    {
        public int Version { get; set; }
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();   // nombre -> SHA-256 hex
    }

    // Integridad de data\: los tweaks ejecutan comandos como administrador y deciden que es PRO, asi que un JSON de
    // disco solo vale si lo firmo el autor. manifest.json lleva la version y el SHA-256 de cada fichero y manifest.sig
    // es su firma ECDSA P-256 (base64) hecha con la clave privada, que nunca sale del PC del autor (tools/OtaSigner).
    public static class SignedData
    {
        // Clave publica (SubjectPublicKeyInfo en base64). La escribe aqui "OtaSigner keygen <carpeta> --source <este fichero>".
        // Vacia = no se confia en ningun fichero de disco (se usan siempre los JSON incrustados) y la OTA queda desactivada.
        public const string PublicKey = "";

        public const string ManifestFile = "manifest.json";
        public const string SignatureFile = "manifest.sig";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static bool VerifySignature(byte[] data, byte[] signature, string publicKeyBase64)
        {
            if (data == null || signature == null || string.IsNullOrWhiteSpace(publicKeyBase64)) return false;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch (FormatException) { return false; }          // clave mal copiada
            catch (CryptographicException) { return false; }    // clave o firma con formato invalido
        }

        public static OtaManifest ParseManifest(byte[] bytes) => JsonSerializer.Deserialize<OtaManifest>(bytes, Json);

        // Manifiesto de la carpeta si su firma es valida con la clave; null (y el motivo) si falta, esta roto o no cuadra
        public static OtaManifest ReadVerifiedManifest(string dir, string publicKey, out string reason)
        {
            try
            {
                var manifestPath = Path.Combine(dir, ManifestFile);
                var signaturePath = Path.Combine(dir, SignatureFile);
                if (!File.Exists(manifestPath) || !File.Exists(signaturePath)) { reason = "sin manifiesto firmado"; return null; }
                var bytes = File.ReadAllBytes(manifestPath);
                var signature = Convert.FromBase64String(File.ReadAllText(signaturePath).Trim());
                if (!VerifySignature(bytes, signature, publicKey)) { reason = "firma no valida"; return null; }
                var manifest = ParseManifest(bytes);
                if (manifest?.Files == null) { reason = "manifiesto vacio"; return null; }
                reason = null;
                return manifest;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is JsonException)
            {
                reason = "manifiesto ilegible: " + ex.Message;
                return null;
            }
        }

        // Bytes de dir\file solo si coinciden con el SHA-256 del manifiesto firmado. Se hashean los mismos bytes que se
        // devuelven: no hay hueco para cambiar el fichero entre la comprobacion y la lectura.
        // null con reason null = el fichero no existe; null con reason = existe pero se rechaza.
        public static byte[] ReadVerifiedFile(string dir, string file, string publicKey, out int version, out string reason)
        {
            version = 0;
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) { reason = null; return null; }
            var manifest = ReadVerifiedManifest(dir, publicKey, out reason);
            if (manifest == null) return null;
            if (!manifest.Files.TryGetValue(file, out var expected)) { reason = "no figura en el manifiesto firmado"; return null; }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { reason = "ilegible: " + ex.Message; return null; }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = "manipulado (el SHA-256 no coincide con el manifiesto)";
                return null;
            }
            version = manifest.Version;
            return bytes;
        }
    }
}
