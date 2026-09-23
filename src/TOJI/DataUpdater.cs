using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Toji
{
    public enum OtaStatus { Disabled, UpToDate, Updated, Failed }

    public class OtaResult
    {
        public OtaStatus Status { get; set; }
        public int Version { get; set; }
        public string Message { get; set; }
    }

    // Actualizacion OTA de data\*.json. Los tweaks contienen comandos que se ejecutan como administrador,
    // asi que SOLO se acepta un manifest.json firmado (ECDSA P-256) con la clave privada del autor y cuyos
    // hashes SHA-256 coincidan con cada fichero descargado. La firma (manifest.sig) se guarda junto a los datos:
    // el motor la vuelve a comprobar en cada carga (SignedData). Genera las claves y firma con tools/OtaSigner.
    public class DataUpdater
    {
        // URL publica donde se publican manifest.json, manifest.sig y los JSON (p. ej. GitHub Pages o Cloudflare R2).
        // Vacia = OTA desactivada.
        public const string DefaultBaseUrl = "";

        public static readonly string[] AllowedFiles = { "tweaks.json", "apps.json", "features.json", "profiles.json" };

        private readonly TojiEngine _eng;
        private readonly string _baseUrl;
        private readonly string _publicKey;
        private readonly HttpMessageHandler _handler;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // publicKey null = la del motor (SignedData.PublicKey en la app)
        public DataUpdater(TojiEngine eng, string baseUrl = DefaultBaseUrl, string publicKey = null, HttpMessageHandler handler = null)
        {
            _eng = eng;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.TrimEnd('/') + "/";
            _publicKey = publicKey ?? eng.DataPublicKey;
            _handler = handler;
        }

        private string DataDir => _eng.DataDir;

        // Version que ya tiene el cliente: la de los datos incrustados en el .exe o la de data\ si su manifiesto esta
        // bien firmado. Un manifest.json sin firma (p. ej. con version 9999) no cuenta: no puede bloquear las OTA
        public int LocalVersion() =>
            Math.Max(TojiEngine.EmbeddedDataVersion, SignedData.ReadVerifiedManifest(DataDir, _publicKey, out _)?.Version ?? 0);

        public async Task<OtaResult> CheckAndUpdateAsync(CancellationToken ct = default)
        {
            if (_baseUrl.Length == 0 || string.IsNullOrWhiteSpace(_publicKey))
                return new OtaResult { Status = OtaStatus.Disabled, Message = "OTA sin configurar" };
            try
            {
                using var http = _handler != null ? new HttpClient(_handler, false) : new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("TOJI-Celestial-Optimizer");

                var manifestBytes = await http.GetByteArrayAsync(_baseUrl + "manifest.json", ct);
                var signature = Convert.FromBase64String((await http.GetStringAsync(_baseUrl + "manifest.sig", ct)).Trim());
                if (!VerifySignature(manifestBytes, signature, _publicKey))
                    throw new InvalidDataException("La firma del manifiesto no es válida: actualización rechazada.");

                var manifest = SignedData.ParseManifest(manifestBytes) ?? throw new InvalidDataException("Manifiesto vacío.");
                if (manifest.Version <= LocalVersion())
                    return new OtaResult { Status = OtaStatus.UpToDate, Version = manifest.Version };
                if (manifest.Files.Keys.Any(f => !AllowedFiles.Contains(f)))
                    throw new InvalidDataException("El manifiesto incluye ficheros no permitidos.");

                // 1) Descargar y verificar TODO en una carpeta temporal antes de tocar data\
                var staging = Path.Combine(_eng.Root, "ota-staging");
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                foreach (var (file, hash) in manifest.Files)
                {
                    var bytes = await http.GetByteArrayAsync(_baseUrl + file, ct);
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{file}: el SHA-256 no coincide con el manifiesto.");
                    ValidateJson(file, bytes);
                    File.WriteAllBytes(Path.Combine(staging, file), bytes);
                }

                // 2) Fusion de perfiles: los perfiles propios que siguieran en data\profiles.json pasan al fichero
                //    del usuario ANTES de sustituirlo, asi la descarga nunca los borra
                if (manifest.Files.ContainsKey("profiles.json"))
                    _eng.MoveCustomProfilesToUserFile(Path.Combine(DataDir, "profiles.json"));

                // 3) Sustituir
                Directory.CreateDirectory(DataDir);
                foreach (var file in manifest.Files.Keys)
                    File.Move(Path.Combine(staging, file), Path.Combine(DataDir, file), true);
                File.WriteAllBytes(Path.Combine(DataDir, SignedData.ManifestFile), manifestBytes);
                File.WriteAllText(Path.Combine(DataDir, SignedData.SignatureFile), Convert.ToBase64String(signature));
                Directory.Delete(staging, true);

                _eng.Log($"OTA: datos actualizados a la versión {manifest.Version}");
                return new OtaResult { Status = OtaStatus.Updated, Version = manifest.Version };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                _eng.Log("OTA fallida: " + ex.Message);
                return new OtaResult { Status = OtaStatus.Failed, Message = ex.Message };
            }
        }

        public static bool VerifySignature(byte[] data, byte[] signature, string publicKeyBase64) =>
            SignedData.VerifySignature(data, signature, publicKeyBase64);

        // Un JSON roto o vacio nunca debe sustituir a uno bueno
        private void ValidateJson(string file, byte[] bytes)
        {
            if (file == "tweaks.json")
            {
                var t = JsonSerializer.Deserialize<TweaksFile>(bytes, _json);
                if (t?.Tweaks == null || t.Tweaks.Count == 0 || t.Tweaks.Any(x => string.IsNullOrEmpty(x.Id)))
                    throw new InvalidDataException("tweaks.json no es válido.");
            }
            else if (file == "profiles.json")
            {
                var p = JsonSerializer.Deserialize<ProfilesFile>(bytes, _json);
                if (p?.Profiles == null) throw new InvalidDataException("profiles.json no es válido.");
            }
            else
            {
                using var doc = JsonDocument.Parse(bytes);
            }
        }
    }
}
