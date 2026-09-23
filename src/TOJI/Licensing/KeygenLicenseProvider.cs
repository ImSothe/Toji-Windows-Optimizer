using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Toji.Licensing
{
    // Cliente de la API de Keygen (validar clave con huella y activar maquina) y del Worker de pruebas.
    // Validar y activar con la propia clave no necesita secretos: el token de administrador vive solo en el Worker.
    public sealed class KeygenLicenseProvider : ILicenseProvider
    {
        private const string JsonApi = "application/vnd.api+json";
        private readonly string _accountUrl;
        private readonly string _workerUrl;
        private readonly HttpClient _http;

        public string Name => "Keygen";

        public KeygenLicenseProvider(string accountId, string trialWorkerUrl, HttpMessageHandler handler = null)
        {
            _accountUrl = "https://api.keygen.sh/v1/accounts/" + accountId + "/";
            _workerUrl = (trialWorkerUrl ?? "").TrimEnd('/') + "/";
            _http = handler != null ? new HttpClient(handler, false) : new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(20);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TOJI-Celestial-Optimizer");
        }

        public async Task<ProviderResult> ValidateAsync(string key, string fingerprint, CancellationToken ct = default)
            => (await ValidateRawAsync(key, fingerprint, ct)).result;

        public async Task<ProviderResult> ActivateAsync(string key, string fingerprint, CancellationToken ct = default)
        {
            var (result, licenseId, code) = await ValidateRawAsync(key, fingerprint, ct);
            if (result.Outcome != ProviderOutcome.NotActivatedHere || licenseId == null) return result;

            // Licencia valida pero sin esta maquina: activarla (limite y unicidad los aplica la politica de Keygen)
            var body = new
            {
                data = new
                {
                    type = "machines",
                    attributes = new { fingerprint, platform = "Windows", name = Environment.MachineName },
                    relationships = new { license = new { data = new { type = "licenses", id = licenseId } } }
                }
            };
            try
            {
                using var req = JsonRequest(HttpMethod.Post, _accountUrl + "machines", body);
                req.Headers.Authorization = new AuthenticationHeaderValue("License", key);
                using var resp = await _http.SendAsync(req, ct);
                if ((int)resp.StatusCode == 422)
                {
                    var err = await resp.Content.ReadAsStringAsync(ct);
                    return new ProviderResult { Outcome = ProviderOutcome.TooManyMachines, Message = err };
                }
                if (!resp.IsSuccessStatusCode)
                    return new ProviderResult { Outcome = ProviderOutcome.Error, Message = $"Activación rechazada ({(int)resp.StatusCode})." };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = ex.Message };
            }
            return (await ValidateRawAsync(key, fingerprint, ct)).result;
        }

        public async Task<ProviderResult> StartTrialAsync(string fingerprint, CancellationToken ct = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _workerUrl + "trial")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { fingerprint }), Encoding.UTF8, "application/json")
                };
                using var resp = await _http.SendAsync(req, ct);
                if ((int)resp.StatusCode == 409) return new ProviderResult { Outcome = ProviderOutcome.TrialAlreadyUsed };
                if (!resp.IsSuccessStatusCode)
                    return new ProviderResult { Outcome = ProviderOutcome.Error, Message = $"No se pudo iniciar la prueba ({(int)resp.StatusCode})." };
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                return new ProviderResult
                {
                    Outcome = ProviderOutcome.Valid, IsTrial = true,
                    Key = root.GetProperty("key").GetString(),
                    ExpiresAtUtc = ReadDate(root, "expiry")
                };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = ex.Message };
            }
        }

        // POST licenses/actions/validate-key con scope de huella: comprueba clave, caducidad, suspension y maquina
        private async Task<(ProviderResult result, string licenseId, string code)> ValidateRawAsync(string key, string fingerprint, CancellationToken ct)
        {
            try
            {
                var body = new { meta = new { key, scope = new { fingerprint } } };
                using var req = JsonRequest(HttpMethod.Post, _accountUrl + "licenses/actions/validate-key", body);
                using var resp = await _http.SendAsync(req, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                if ((int)resp.StatusCode >= 500)
                    return (new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = $"Servidor de licencias no disponible ({(int)resp.StatusCode})." }, null, null);

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("meta", out var meta))
                    return (new ProviderResult { Outcome = ProviderOutcome.Error, Message = "Respuesta de licencias no válida." }, null, null);
                var code = meta.TryGetProperty("code", out var c) ? c.GetString() : null;
                bool valid = meta.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;

                string id = null; DateTime? expiry = null; bool trial = false;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (data.TryGetProperty("attributes", out var attrs))
                    {
                        expiry = ReadDate(attrs, "expiry");
                        // El Worker marca las licencias de prueba con metadata.trial = true
                        trial = attrs.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object
                                && md.TryGetProperty("trial", out var t) && t.ValueKind == JsonValueKind.True;
                    }
                }

                var outcome = valid ? ProviderOutcome.Valid : code switch
                {
                    "EXPIRED" => ProviderOutcome.Expired,
                    "SUSPENDED" or "BANNED" => ProviderOutcome.Suspended,
                    "NOT_FOUND" => ProviderOutcome.NotFound,
                    "NO_MACHINE" or "NO_MACHINES" or "FINGERPRINT_SCOPE_MISMATCH" => ProviderOutcome.NotActivatedHere,
                    "TOO_MANY_MACHINES" => ProviderOutcome.TooManyMachines,
                    _ => ProviderOutcome.Error
                };
                var detail = meta.TryGetProperty("detail", out var d) ? d.GetString() : code;
                return (new ProviderResult { Outcome = outcome, Key = key, IsTrial = trial, ExpiresAtUtc = expiry, Message = detail }, id, code);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return (new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = ex.Message }, null, null);
            }
            catch (JsonException)
            {
                return (new ProviderResult { Outcome = ProviderOutcome.Error, Message = "Respuesta de licencias no válida." }, null, null);
            }
        }

        private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body)
        {
            var req = new HttpRequestMessage(method, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApi);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApi));
            return req;
        }

        private static DateTime? ReadDate(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
                ? dt : (DateTime?)null;
    }
}
