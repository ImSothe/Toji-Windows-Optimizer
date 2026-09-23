using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Toji.Licensing
{
    // Release sin cuenta de Keygen configurada: todo se ve, pero no se puede activar (fallo cerrado)
    public sealed class UnconfiguredLicenseProvider : ILicenseProvider
    {
        private static readonly ProviderResult NotConfigured = new ProviderResult { Outcome = ProviderOutcome.NotConfigured };
        public string Name => "Sin configurar";
        public Task<ProviderResult> ActivateAsync(string key, string fingerprint, CancellationToken ct = default) => Task.FromResult(NotConfigured);
        public Task<ProviderResult> ValidateAsync(string key, string fingerprint, CancellationToken ct = default) => Task.FromResult(NotConfigured);
        public Task<ProviderResult> StartTrialAsync(string fingerprint, CancellationToken ct = default) => Task.FromResult(NotConfigured);
    }

#if DEBUG
    // SOLO en compilaciones Debug: simula Keygen para probar el flujo completo sin cuenta.
    //   TOJI-DEMO-PRO... -> licencia permanente   |   TOJI-DEMO-REVOKED -> revocada   |   otra -> no existe
    //   La prueba se registra por huella en demo-trials.txt (una por PC, como UNIQUE_PER_POLICY)
    public sealed class DemoLicenseProvider : ILicenseProvider
    {
        private readonly string _trialsFile;
        public string Name => "Demo";
        public DemoLicenseProvider(string root) { _trialsFile = Path.Combine(root, "demo-trials.txt"); }

        public Task<ProviderResult> ActivateAsync(string key, string fingerprint, CancellationToken ct = default) => ValidateAsync(key, fingerprint, ct);

        public async Task<ProviderResult> ValidateAsync(string key, string fingerprint, CancellationToken ct = default)
        {
            await Task.Delay(400, ct);   // simula la red
            key = (key ?? "").Trim().ToUpperInvariant();
            if (key == "TOJI-DEMO-REVOKED") return new ProviderResult { Outcome = ProviderOutcome.Suspended, Key = key };
            if (key.StartsWith("TOJI-DEMO-TRIAL-"))
            {
                var start = TrialStart(fingerprint);
                if (start == null) return new ProviderResult { Outcome = ProviderOutcome.NotFound, Key = key };
                var exp = start.Value.AddDays(LicenseConfig.TrialDays);
                return new ProviderResult { Outcome = exp > DateTime.UtcNow ? ProviderOutcome.Valid : ProviderOutcome.Expired, Key = key, IsTrial = true, ExpiresAtUtc = exp };
            }
            if (key.StartsWith("TOJI-DEMO-PRO")) return new ProviderResult { Outcome = ProviderOutcome.Valid, Key = key };
            return new ProviderResult { Outcome = ProviderOutcome.NotFound, Key = key };
        }

        public async Task<ProviderResult> StartTrialAsync(string fingerprint, CancellationToken ct = default)
        {
            await Task.Delay(400, ct);
            if (TrialStart(fingerprint) != null) return new ProviderResult { Outcome = ProviderOutcome.TrialAlreadyUsed };
            var now = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_trialsFile));
            File.AppendAllText(_trialsFile, fingerprint + "|" + now.ToString("o") + Environment.NewLine);
            return new ProviderResult
            {
                Outcome = ProviderOutcome.Valid, IsTrial = true, ExpiresAtUtc = now.AddDays(LicenseConfig.TrialDays),
                Key = "TOJI-DEMO-TRIAL-" + fingerprint.Substring(0, Math.Min(8, fingerprint.Length))
            };
        }

        private DateTime? TrialStart(string fingerprint)
        {
            if (!File.Exists(_trialsFile)) return null;
            var line = File.ReadAllLines(_trialsFile).FirstOrDefault(l => l.StartsWith(fingerprint + "|"));
            return line != null && DateTime.TryParse(line.Split('|')[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : (DateTime?)null;
        }
    }
#endif
}
