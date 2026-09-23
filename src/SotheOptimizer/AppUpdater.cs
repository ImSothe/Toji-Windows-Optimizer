using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sothe
{
    public enum UpdateStatus { Disabled, UpToDate, Available, Staged, Failed }

    public sealed class UpdateInfo
    {
        public UpdateStatus Status { get; set; }
        public Version Version { get; set; }
        public string ExeUrl { get; set; }
        public string Sha256 { get; set; }
        public string FileName { get; set; }
        public string Notes { get; set; }
        public string Message { get; set; }
    }

    // release.json que publica el autor junto a la release (firmado como los datos OTA)
    public sealed class ReleaseManifest
    {
        public string Version { get; set; }
        public string File { get; set; }      // nombre del .exe entre los assets
        public string Sha256 { get; set; }    // SHA-256 hex del .exe
        public string Notes { get; set; }
    }

    // Auto-actualizacion segura. Regla de oro: NUNCA se ejecuta un binario descargado sin verificarlo antes.
    // Flujo: se consulta la ultima release en GitHub, se descarga release.json + release.sig y se comprueba que la
    // firma (ECDSA P-256) es del autor con la clave publica incrustada (SignedData.PublicKey). Solo si la firma es
    // valida y el SHA-256 del .exe descargado coincide con el del manifiesto firmado, se deja preparado; y se instala
    // al ARRANCAR (nunca a mitad de sesion, para no interrumpir un APLICAR sobre el registro). Un GitHub secuestrado
    // o un ataque de red no pueden colar un binario que la app llegue a ejecutar: sin firma valida, se ignora.
    public sealed class AppUpdater
    {
        // "owner/repo" de las releases PUBLICAS (p. ej. un repo aparte solo para descargas, ver BUILD.md). Vacio =
        // auto-actualizacion desactivada. La firma va aparte de GitHub: aunque la cuenta de GitHub se viera comprometida,
        // sin la clave privada del autor no se puede firmar una release, y sin firma valida la app no instala nada.
        public const string GitHubRepo = "";
        public const string DefaultApiBase = "https://api.github.com";

        public const string PendingExe = "SotheOptimizer.pending.exe";
        public const string PendingInfo = "pending.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly Version _current;
        private readonly string _repo;
        private readonly string _publicKey;
        private readonly string _apiBase;
        private readonly HttpMessageHandler _handler;

        public AppUpdater(Version current, string repo = GitHubRepo, string publicKey = null,
            HttpMessageHandler handler = null, string apiBase = DefaultApiBase)
        {
            _current = current ?? new Version(0, 0, 0);
            _repo = (repo ?? "").Trim().Trim('/');
            _publicKey = publicKey ?? SignedData.PublicKey;
            _apiBase = (apiBase ?? DefaultApiBase).TrimEnd('/');
            _handler = handler;
        }

        public bool IsConfigured => _repo.Length > 0 && !string.IsNullOrWhiteSpace(_publicKey);

        // Consulta la ultima release, verifica la firma del manifiesto y devuelve si hay una version mas nueva.
        // No descarga el .exe todavia.
        public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return new UpdateInfo { Status = UpdateStatus.Disabled, Message = "Auto-actualizacion sin configurar" };
            try
            {
                using var http = _handler != null ? new HttpClient(_handler, false) : new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sothe-Optimizer");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var indexJson = await http.GetStringAsync($"{_apiBase}/repos/{_repo}/releases/latest", ct);
                var (manifestUrl, sigUrl, assetUrl) = FindAssets(indexJson);
                if (manifestUrl == null || sigUrl == null)
                    return new UpdateInfo { Status = UpdateStatus.Failed, Message = "La release no incluye release.json firmado." };

                var manifestBytes = await http.GetByteArrayAsync(manifestUrl, ct);
                var signature = Convert.FromBase64String((await http.GetStringAsync(sigUrl, ct)).Trim());
                if (!SignedData.VerifySignature(manifestBytes, signature, _publicKey))
                    return new UpdateInfo { Status = UpdateStatus.Failed, Message = "La firma de la actualizacion no es valida: se ignora." };

                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, Json);
                if (manifest == null || !Version.TryParse((manifest.Version ?? "").TrimStart('v', 'V'), out var version)
                    || string.IsNullOrWhiteSpace(manifest.Sha256) || string.IsNullOrWhiteSpace(manifest.File))
                    return new UpdateInfo { Status = UpdateStatus.Failed, Message = "Manifiesto de release invalido." };

                // El .exe se busca por el nombre firmado, no por el que diga GitHub
                var exeUrl = assetUrl != null && AssetName(indexJson, assetUrl) == manifest.File ? assetUrl : UrlOfAsset(indexJson, manifest.File);
                if (exeUrl == null)
                    return new UpdateInfo { Status = UpdateStatus.Failed, Message = $"La release no incluye {manifest.File}." };

                if (version <= _current)
                    return new UpdateInfo { Status = UpdateStatus.UpToDate, Version = version };

                return new UpdateInfo
                {
                    Status = UpdateStatus.Available, Version = version, ExeUrl = exeUrl,
                    Sha256 = manifest.Sha256, FileName = manifest.File, Notes = manifest.Notes
                };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested || ex is JsonException)
            {
                return new UpdateInfo { Status = UpdateStatus.Failed, Message = ex.Message };
            }
        }

        // Descarga el .exe, comprueba que su SHA-256 coincide con el del manifiesto FIRMADO y lo deja preparado en
        // stageDir. Si el hash no cuadra, no se guarda nada. La instalacion ocurre en el siguiente arranque.
        public async Task<UpdateInfo> DownloadAndStageAsync(UpdateInfo info, string stageDir, CancellationToken ct = default)
        {
            if (info == null || info.Status != UpdateStatus.Available)
                return info ?? new UpdateInfo { Status = UpdateStatus.Failed };
            try
            {
                using var http = _handler != null ? new HttpClient(_handler, false) : new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(5);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sothe-Optimizer");

                var bytes = await http.GetByteArrayAsync(info.ExeUrl, ct);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new UpdateInfo { Status = UpdateStatus.Failed, Message = "El instalador descargado no coincide con la firma: descartado." };

                Directory.CreateDirectory(stageDir);
                var tmp = Path.Combine(stageDir, PendingExe + ".part");
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                var staged = Path.Combine(stageDir, PendingExe);
                File.Move(tmp, staged, true);
                File.WriteAllText(Path.Combine(stageDir, PendingInfo),
                    JsonSerializer.Serialize(new PendingUpdate { Version = info.Version.ToString(), Sha256 = info.Sha256 }));
                return new UpdateInfo { Status = UpdateStatus.Staged, Version = info.Version, Notes = info.Notes };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested || ex is IOException)
            {
                return new UpdateInfo { Status = UpdateStatus.Failed, Message = ex.Message };
            }
        }

        public sealed class PendingUpdate { public string Version { get; set; } public string Sha256 { get; set; } }

        // ---- Instalacion al arrancar (antes de abrir ninguna ventana) ----
        // Si hay un .exe preparado, mas nuevo y cuyo SHA-256 sigue coincidiendo con lo firmado, lanza el script que
        // sustituye el .exe actual y reinicia; el llamador debe cerrar la app a continuacion. Devuelve false si no hay
        // nada que instalar o si la comprobacion falla (en cuyo caso se limpia lo preparado, nunca se ejecuta).
        public static bool TryApplyStaged(string currentExe, string stageDir, Version current, out string error)
        {
            error = null;
            var staged = Path.Combine(stageDir, PendingExe);
            var infoPath = Path.Combine(stageDir, PendingInfo);
            if (!File.Exists(staged) || !File.Exists(infoPath)) return false;
            try
            {
                var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(infoPath), Json);
                bool newer = pending != null && Version.TryParse(pending.Version, out var v) && v > (current ?? new Version(0, 0, 0));
                bool intact = pending != null && string.Equals(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(staged))), pending.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!newer || !intact)
                {
                    // Preparado invalido o ya aplicado: se descarta, nunca se ejecuta
                    Discard(stageDir);
                    return false;
                }
                if (!OperatingSystem.IsWindows()) return false;
                var script = Path.Combine(stageDir, "apply-update.cmd");
                File.WriteAllText(script, BuildSwapScript(Environment.ProcessId, staged, currentExe, infoPath));
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = stageDir });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void Discard(string stageDir)
        {
            foreach (var f in new[] { PendingExe, PendingInfo })
                try { File.Delete(Path.Combine(stageDir, f)); } catch { }
        }

        // Script .cmd: espera a que el proceso actual muera, reemplaza el .exe, lo relanza y se limpia.
        // Solo mueve un fichero YA verificado (TryApplyStaged comprueba el SHA-256 antes de escribir este script).
        public static string BuildSwapScript(int pid, string stagedExe, string targetExe, string pendingInfo) =>
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)\r\n" +
            $"move /y \"{stagedExe}\" \"{targetExe}\" >nul\r\n" +
            $"del /q \"{pendingInfo}\" >nul 2>&1\r\n" +
            $"start \"\" \"{targetExe}\"\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n";

        // ---- Lectura del indice de releases de GitHub (assets externos: nombres tratados como datos) ----
        private static (string manifest, string sig, string exe) FindAssets(string indexJson)
        {
            string manifest = UrlOfAsset(indexJson, "release.json");
            string sig = UrlOfAsset(indexJson, "release.sig");
            string exe = null;
            using var doc = JsonDocument.Parse(indexJson);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (Name(a).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { exe = Url(a); break; }
            return (manifest, sig, exe);
        }

        private static string UrlOfAsset(string indexJson, string name)
        {
            using var doc = JsonDocument.Parse(indexJson);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (string.Equals(Name(a), name, StringComparison.OrdinalIgnoreCase)) return Url(a);
            return null;
        }

        private static string AssetName(string indexJson, string url)
        {
            using var doc = JsonDocument.Parse(indexJson);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (Url(a) == url) return Name(a);
            return null;
        }

        private static string Name(JsonElement asset) => asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        private static string Url(JsonElement asset) => asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
    }
}
