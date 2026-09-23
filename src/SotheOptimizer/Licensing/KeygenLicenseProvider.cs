using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sothe.Licensing
{
    // Cliente de la API de Keygen: validar la clave con la huella del PC y activar la maquina (node-locked: la politica
    // limita cada clave a 1 maquina). Todo con la propia clave, sin secretos: el token de administrador nunca va en la app.
    // Requisitos de la politica en Keygen: Max machines = 1, Floating = no, Authentication strategy = LICENSE (o MIXED)
    // para poder activar con "Authorization: License <clave>".
    public sealed class KeygenLicenseProvider : ILicenseProvider
    {
        private const string JsonApi = "application/vnd.api+json";
        private readonly string _accountUrl;
        private readonly string _productId;
        private readonly string _trialPolicyId;
        private readonly string _workerUrl;
        private readonly HttpClient _http;

        public string Name => "Keygen";
        public bool SupportsSelfTrial => _workerUrl.Length > 0;

        public KeygenLicenseProvider(string accountId, string productId, string trialPolicyId, string trialWorkerUrl = "", HttpMessageHandler handler = null)
        {
            _accountUrl = "https://api.keygen.sh/v1/accounts/" + accountId + "/";
            _productId = productId ?? "";
            _trialPolicyId = trialPolicyId ?? "";
            _workerUrl = string.IsNullOrWhiteSpace(trialWorkerUrl) ? "" : trialWorkerUrl.TrimEnd('/') + "/";
            _http = handler != null ? new HttpClient(handler, false) : new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(20);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sothe-Optimizer");
        }

        public async Task<ProviderResult> ValidateAsync(string key, string fingerprint, CancellationToken ct = default)
            => (await ValidateRawAsync(key, fingerprint, ct)).result;

        public async Task<ProviderResult> ActivateAsync(string key, string fingerprint, CancellationToken ct = default)
        {
            var (result, licenseId) = await ValidateRawAsync(key, fingerprint, ct);
            if (result.Outcome != ProviderOutcome.NotActivatedHere || licenseId == null) return result;

            // Clave valida pero sin esta maquina: se activa. El limite de 1 PC y la unicidad los aplica la politica de Keygen
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
                int status = (int)resp.StatusCode;
                if (status == 422)
                {
                    var code = FirstErrorCode(await resp.Content.ReadAsStringAsync(ct));
                    // FINGERPRINT_TAKEN: este PC ya esta registrado en otra clave de la misma politica (una prueba por PC)
                    if (code == "FINGERPRINT_TAKEN")
                        return result.IsTrial
                            ? new ProviderResult { Outcome = ProviderOutcome.TrialAlreadyUsed }
                            : new ProviderResult { Outcome = ProviderOutcome.Error, Message = "Este PC ya está registrado con otra licencia." };
                    return new ProviderResult { Outcome = ProviderOutcome.TooManyMachines, Message = code };
                }
                if (status == 401 || status == 403)
                    return new ProviderResult { Outcome = ProviderOutcome.Error,
                        Message = $"El servidor de licencias no permite activar con esta clave ({status}). Contacta con soporte." };
                if (status >= 500)
                    return new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = $"Servidor de licencias no disponible ({status})." };
                if (!resp.IsSuccessStatusCode)
                    return new ProviderResult { Outcome = ProviderOutcome.Error, Message = $"Activación rechazada ({status})." };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = ex.Message };
            }
            return (await ValidateRawAsync(key, fingerprint, ct)).result;
        }

        // Prueba de un clic via Worker (opcional). Sin Worker, las pruebas son claves de la politica de prueba
        public async Task<ProviderResult> StartTrialAsync(string fingerprint, CancellationToken ct = default)
        {
            if (!SupportsSelfTrial) return new ProviderResult { Outcome = ProviderOutcome.NotConfigured };
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

        // POST licenses/actions/validate-key acotado a este producto y a la huella de este PC: comprueba que la clave
        // existe y es de Sothe Optimizer, caducidad, suspension y que la maquina activada es esta
        private async Task<(ProviderResult result, string licenseId)> ValidateRawAsync(string key, string fingerprint, CancellationToken ct)
        {
            try
            {
                object scope = _productId.Length > 0 ? new { fingerprint, product = _productId } : new { fingerprint };
                using var req = JsonRequest(HttpMethod.Post, _accountUrl + "licenses/actions/validate-key", new { meta = new { key, scope } });
                using var resp = await _http.SendAsync(req, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                if ((int)resp.StatusCode >= 500)
                    return (new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = $"Servidor de licencias no disponible ({(int)resp.StatusCode})." }, null);

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("meta", out var meta))
                    return (new ProviderResult { Outcome = ProviderOutcome.Error, Message = "Respuesta de licencias no válida." }, null);
                var code = meta.TryGetProperty("code", out var c) ? c.GetString() : null;
                bool valid = meta.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;

                string id = null; DateTime? expiry = null; bool trial = false;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (data.TryGetProperty("attributes", out var attrs))
                    {
                        expiry = ReadDate(attrs, "expiry");
                        // Pruebas del Worker: metadata.trial = true
                        trial = attrs.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object
                                && md.TryGetProperty("trial", out var t) && t.ValueKind == JsonValueKind.True;
                    }
                    // Claves de prueba de 3 meses: su politica es la de prueba
                    trial |= _trialPolicyId.Length > 0 && string.Equals(PolicyId(data), _trialPolicyId, StringComparison.OrdinalIgnoreCase);
                }

                var outcome = valid ? ProviderOutcome.Valid : code switch
                {
                    "EXPIRED" => ProviderOutcome.Expired,
                    "SUSPENDED" or "BANNED" => ProviderOutcome.Suspended,
                    "NOT_FOUND" => ProviderOutcome.NotFound,
                    "PRODUCT_SCOPE_MISMATCH" or "POLICY_SCOPE_MISMATCH" => ProviderOutcome.WrongProduct,
                    "NO_MACHINE" or "NO_MACHINES" or "FINGERPRINT_SCOPE_MISMATCH" => ProviderOutcome.NotActivatedHere,
                    "TOO_MANY_MACHINES" => ProviderOutcome.TooManyMachines,
                    _ => ProviderOutcome.Error
                };
                var detail = meta.TryGetProperty("detail", out var d) ? d.GetString() : code;
                return (new ProviderResult { Outcome = outcome, Key = key, IsTrial = trial, ExpiresAtUtc = expiry, Message = detail }, id);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return (new ProviderResult { Outcome = ProviderOutcome.NetworkError, Message = ex.Message }, null);
            }
            catch (JsonException)
            {
                return (new ProviderResult { Outcome = ProviderOutcome.Error, Message = "Respuesta de licencias no válida." }, null);
            }
        }

        // data.relationships.policy.data.id de una licencia
        private static string PolicyId(JsonElement license) =>
            license.TryGetProperty("relationships", out var rel) && rel.TryGetProperty("policy", out var pol)
            && pol.TryGetProperty("data", out var pd) && pd.ValueKind == JsonValueKind.Object && pd.TryGetProperty("id", out var pid)
                ? pid.GetString() : null;

        // errors[0].code de una respuesta de error JSON:API
        private static string FirstErrorCode(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
                    foreach (var e in errs.EnumerateArray())
                        if (e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
            }
            catch (JsonException) { }
            return null;
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
