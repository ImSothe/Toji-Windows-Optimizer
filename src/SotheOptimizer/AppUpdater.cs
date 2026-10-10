using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        public long Size { get; set; }
        public string Notes { get; set; }
        public string Message { get; set; }
    }

    // release.json que publica el autor junto a la release (firmado como los datos OTA)
    public sealed class ReleaseManifest
    {
        public string Version { get; set; }
        public string File { get; set; }      // nombre del .exe entre los assets
        public string Sha256 { get; set; }    // SHA-256 hex del .exe
        public long Size { get; set; }        // bytes del .exe (opcional; si falta se aplica el tope maximo)
        public string Notes { get; set; }
    }

    // Auto-actualizacion segura. Regla de oro: NUNCA se ejecuta un binario descargado sin verificarlo antes.
    // Flujo: se consulta la ultima release en GitHub, se descarga release.json + release.sig y se comprueba que la
    // firma (ECDSA P-256) es del autor con la clave publica incrustada (SignedData.PublicKey). Solo si la firma es
    // valida y el SHA-256 del .exe descargado coincide con el del manifiesto firmado, se deja preparado; y se instala
    // al ARRANCAR o cuando no hay nada en curso (nunca a mitad de un APLICAR sobre el registro). Un GitHub secuestrado
    // o un ataque de red no pueden colar un binario que la app llegue a ejecutar: sin firma valida, se ignora.
    //
    // Endurecimiento adicional: solo HTTPS y solo hosts de GitHub; la descarga va por streaming a disco con el hash
    // calculado sobre la marcha y un tope de tamano (no se carga el .exe entero en memoria); el script que sustituye
    // el .exe hace copia de seguridad, tiene tiempo maximo y deshace el cambio si falla.
    public sealed class AppUpdater
    {
        // "owner/repo" de las releases PUBLICAS (un repo aparte solo para descargas, ver BUILD.md). Vacio =
        // auto-actualizacion desactivada. La firma va aparte de GitHub: aunque la cuenta de GitHub se viera comprometida,
        // sin la clave privada del autor no se puede firmar una release, y sin firma valida la app no instala nada.
        public const string GitHubRepo = "ImSothe/Sothe-Windows-Optimizer";
        public const string DefaultApiBase = "https://api.github.com";

        public const string PendingExe = "SotheOptimizer.pending.exe";
        public const string PendingInfo = "pending.json";

        // Tope de descarga si el manifiesto no declara tamano (el exe actual pesa unos 155 MB)
        public const long MaxExeBytes = 400L * 1024 * 1024;

        // Hosts desde los que se acepta descargar (un sufijo que empieza por "." admite sus subdominios)
        public static readonly string[] DefaultAllowedHosts = { "api.github.com", "github.com", ".githubusercontent.com" };

        private static readonly Regex RepoPattern = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly Version _current;
        private readonly string _repo;
        private readonly string _publicKey;
        private readonly string _apiBase;
        private readonly HttpMessageHandler _handler;
        private readonly string[] _allowedHosts;

        public AppUpdater(Version current, string repo = GitHubRepo, string publicKey = null,
            HttpMessageHandler handler = null, string apiBase = DefaultApiBase, IEnumerable<string> allowedHosts = null)
        {
            _current = current ?? new Version(0, 0, 0);
            _repo = (repo ?? "").Trim().Trim('/');
            _publicKey = publicKey ?? SignedData.PublicKey;
            _apiBase = (apiBase ?? DefaultApiBase).TrimEnd('/');
            _handler = handler;
            _allowedHosts = (allowedHosts ?? DefaultAllowedHosts).Select(h => h.ToLowerInvariant()).ToArray();
        }

        public bool IsConfigured => _repo.Length > 0 && !string.IsNullOrWhiteSpace(_publicKey);

        // Solo HTTPS y solo hosts permitidos: ni http:// ni un servidor ajeno aunque el JSON de GitHub lo pidiera
        public bool IsAllowedUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
            var host = u.Host.ToLowerInvariant();
            return _allowedHosts.Any(a => host == a || (a.StartsWith(".") && host.EndsWith(a, StringComparison.Ordinal)));
        }

        private static UpdateInfo Fail(string message) => new UpdateInfo { Status = UpdateStatus.Failed, Message = message };

        // Errores esperables de red, disco o datos mal formados: un fallo de actualizacion nunca debe tumbar la app
        private static bool IsExpected(Exception ex, CancellationToken ct) =>
            ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested) || ex is JsonException
            || ex is FormatException || ex is UriFormatException || ex is IOException || ex is UnauthorizedAccessException
            || ex is InvalidOperationException;

        // Consulta la ultima release, verifica la firma del manifiesto y devuelve si hay una version mas nueva.
        // No descarga el .exe todavia.
        public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return new UpdateInfo { Status = UpdateStatus.Disabled, Message = "Auto-actualizacion sin configurar" };
            if (!RepoPattern.IsMatch(_repo)) return Fail("Repositorio de actualizaciones no valido.");
            if (!IsAllowedUrl(_apiBase)) return Fail("El servidor de actualizaciones debe ser HTTPS y de GitHub.");
            try
            {
                using var http = _handler != null ? new HttpClient(_handler, false) : new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sothe-Optimizer");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var indexJson = await http.GetStringAsync($"{_apiBase}/repos/{_repo}/releases/latest", ct);
                var (manifestUrl, sigUrl, assetUrl) = FindAssets(indexJson);
                if (manifestUrl == null || sigUrl == null)
                    return Fail("La release no incluye release.json firmado.");
                if (!IsAllowedUrl(manifestUrl) || !IsAllowedUrl(sigUrl))
                    return Fail("La release apunta a una URL no permitida (solo HTTPS de GitHub).");

                var manifestBytes = await http.GetByteArrayAsync(manifestUrl, ct);
                var signature = Convert.FromBase64String((await http.GetStringAsync(sigUrl, ct)).Trim());
                if (!SignedData.VerifySignature(manifestBytes, signature, _publicKey))
                    return Fail("La firma de la actualizacion no es valida: se ignora.");

                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, Json);
                if (manifest == null || !Version.TryParse((manifest.Version ?? "").TrimStart('v', 'V'), out var version)
                    || string.IsNullOrWhiteSpace(manifest.Sha256) || string.IsNullOrWhiteSpace(manifest.File))
                    return Fail("Manifiesto de release invalido.");
                if (manifest.Size < 0 || manifest.Size > MaxExeBytes)
                    return Fail("El tamano declarado en el manifiesto no es valido.");

                // El .exe se busca por el nombre firmado, no por el que diga GitHub
                var exeUrl = assetUrl != null && AssetName(indexJson, assetUrl) == manifest.File ? assetUrl : UrlOfAsset(indexJson, manifest.File);
                if (exeUrl == null)
                    return Fail($"La release no incluye {manifest.File}.");
                if (!IsAllowedUrl(exeUrl))
                    return Fail("El instalador apunta a una URL no permitida (solo HTTPS de GitHub).");

                if (version <= _current)
                    return new UpdateInfo { Status = UpdateStatus.UpToDate, Version = version };

                return new UpdateInfo
                {
                    Status = UpdateStatus.Available, Version = version, ExeUrl = exeUrl,
                    Sha256 = manifest.Sha256, FileName = manifest.File, Size = manifest.Size, Notes = manifest.Notes
                };
            }
            catch (Exception ex) when (IsExpected(ex, ct))
            {
                return Fail(ex.Message);
            }
        }

        // Descarga el .exe (por streaming, con tope de tamano), comprueba que su SHA-256 coincide con el del manifiesto
        // FIRMADO y lo deja preparado en stageDir. Si el hash no cuadra, no se guarda nada. La instalacion ocurre despues.
        public async Task<UpdateInfo> DownloadAndStageAsync(UpdateInfo info, string stageDir, CancellationToken ct = default)
        {
            if (info == null || info.Status != UpdateStatus.Available)
                return info ?? new UpdateInfo { Status = UpdateStatus.Failed };
            if (!IsAllowedUrl(info.ExeUrl)) return Fail("El instalador apunta a una URL no permitida (solo HTTPS de GitHub).");
            long limit = info.Size > 0 ? info.Size : MaxExeBytes;
            string tmp = null;
            try
            {
                using var http = _handler != null ? new HttpClient(_handler, false) : new HttpClient();
                http.Timeout = Timeout.InfiniteTimeSpan;   // el tiempo total lo acota cts: 10 minutos
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sothe-Optimizer");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(10));

                using var resp = await http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength is long declared && declared > limit)
                    return Fail("El instalador es mas grande de lo declarado: descartado.");

                Directory.CreateDirectory(stageDir);
                tmp = Path.Combine(stageDir, PendingExe + ".part");
                long total = 0;
                string hash;
                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        int n;
                        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                        {
                            total += n;
                            if (total > limit) return DiscardPart(tmp, "El instalador supera el tamano permitido: descartado.");
                            hasher.AppendData(buffer, 0, n);
                            await dst.WriteAsync(buffer, 0, n, cts.Token);
                        }
                    }
                    hash = Convert.ToHexString(hasher.GetHashAndReset());
                }
                if (info.Size > 0 && total != info.Size)
                    return DiscardPart(tmp, "El tamano del instalador no coincide con el manifiesto firmado: descartado.");
                if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    return DiscardPart(tmp, "El instalador descargado no coincide con la firma: descartado.");

                var staged = Path.Combine(stageDir, PendingExe);
                File.Move(tmp, staged, true);
                tmp = null;
                File.WriteAllText(Path.Combine(stageDir, PendingInfo),
                    JsonSerializer.Serialize(new PendingUpdate { Version = info.Version.ToString(), Sha256 = info.Sha256 }));
                return new UpdateInfo { Status = UpdateStatus.Staged, Version = info.Version, Notes = info.Notes };
            }
            catch (Exception ex) when (IsExpected(ex, ct))
            {
                if (tmp != null) try { File.Delete(tmp); } catch { }
                return Fail(ex.Message);
            }
        }

        private static UpdateInfo DiscardPart(string tmp, string message)
        {
            try { File.Delete(tmp); } catch { }
            return Fail(message);
        }

        public sealed class PendingUpdate { public string Version { get; set; } public string Sha256 { get; set; } }

        // ---- Instalacion al arrancar (antes de abrir ninguna ventana) ----
        // Si hay un .exe preparado, mas nuevo y cuyo SHA-256 sigue coincidiendo con lo firmado, lanza el script que
        // sustituye el .exe actual y reinicia; el llamador debe cerrar la app a continuacion. Devuelve false si no hay
        // nada que instalar o si la comprobacion falla (en cuyo caso se limpia lo preparado, nunca se ejecuta).
        // stageDir debe ser una carpeta de confianza (SecureStorage.Harden): solo un administrador puede escribir en ella.
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
                bool intact = pending != null && string.Equals(HashFile(staged), pending.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!newer || !intact)
                {
                    // Preparado invalido o ya aplicado: se descarta, nunca se ejecuta
                    Discard(stageDir);
                    return false;
                }
                if (!OperatingSystem.IsWindows()) return false;
                var script = Path.Combine(stageDir, "apply-update.cmd");
                File.WriteAllText(script, BuildSwapScript(Environment.ProcessId, staged, currentExe, infoPath));
                Process.Start(new ProcessStartInfo(SystemPaths.Cmd, $"/c \"{script}\"")
                { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = stageDir });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Hash por streaming: el .exe pesa ~155 MB y no hay por que cargarlo entero en memoria
        private static string HashFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(fs));
        }

        public static void Discard(string stageDir)
        {
            foreach (var f in new[] { PendingExe, PendingInfo })
                try { File.Delete(Path.Combine(stageDir, f)); } catch { }
        }

        // Script .cmd: espera (maximo ~90 s) a que el proceso actual muera, guarda una copia del .exe actual, lo
        // reemplaza, lo relanza y se limpia. Si el reemplazo falla (antivirus, disco lleno...) restaura la copia y
        // relanza la version anterior: la app nunca se queda sin .exe. Solo mueve un fichero YA verificado
        // (TryApplyStaged comprueba el SHA-256 antes de escribir este script).
        public static string BuildSwapScript(int pid, string stagedExe, string targetExe, string pendingInfo)
        {
            string image = Path.GetFileName(targetExe.Replace('\\', '/'));
            string log = Path.Combine(Path.GetDirectoryName(stagedExe.Replace('\\', '/')) ?? "", "update.log").Replace('/', '\\');
            // En un .cmd el % de una ruta se expandiria: se duplica
            string S(string s) => s.Replace("%", "%%");
            return
                "@echo off\r\n" +
                "set /a tries=0\r\n" +
                ":wait\r\n" +
                $"tasklist /fi \"PID eq {pid}\" /nh 2>nul | find /i \"{S(image)}\" >nul\r\n" +
                "if errorlevel 1 goto swap\r\n" +
                "set /a tries+=1\r\n" +
                "if %tries% geq 45 goto leave\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                "goto wait\r\n" +
                ":swap\r\n" +
                $"copy /y \"{S(targetExe)}\" \"{S(targetExe)}.old\" >nul 2>&1\r\n" +
                $"move /y \"{S(stagedExe)}\" \"{S(targetExe)}\" >nul\r\n" +
                "if errorlevel 1 goto failed\r\n" +
                $"del /q \"{S(targetExe)}.old\" >nul 2>&1\r\n" +
                $"del /q \"{S(pendingInfo)}\" >nul 2>&1\r\n" +
                "goto launch\r\n" +
                ":failed\r\n" +
                $"echo %date% %time% no se pudo reemplazar el ejecutable; se conserva la version anterior >> \"{S(log)}\"\r\n" +
                $"if exist \"{S(targetExe)}.old\" move /y \"{S(targetExe)}.old\" \"{S(targetExe)}\" >nul\r\n" +
                ":launch\r\n" +
                $"start \"\" \"{S(targetExe)}\"\r\n" +
                ":leave\r\n" +
                "(goto) 2>nul & del \"%~f0\"\r\n";
        }

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
