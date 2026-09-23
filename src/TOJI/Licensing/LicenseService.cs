using System;
using System.Threading;
using System.Threading.Tasks;

namespace Toji.Licensing
{
    // Estado de licencia de TOJI. Arranca offline desde la cache cifrada y valida online en segundo plano.
    public sealed class LicenseService
    {
        private readonly ILicenseProvider _provider;
        private readonly LicenseStore _store;
        private readonly Func<DateTime> _utcNow;
        private LicenseCache _cache;

        public string Fingerprint { get; }
        public string ProviderName => _provider.Name;
        public LicenseStatus Status { get; private set; }
        public bool HasKey => !string.IsNullOrEmpty(_cache?.Key);
        public event Action<LicenseStatus> StatusChanged;

        public LicenseService(ILicenseProvider provider, LicenseStore store, string fingerprint, Func<DateTime> utcNow = null)
        {
            _provider = provider;
            _store = store;
            Fingerprint = fingerprint;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _cache = _store.Load();
            Recompute();
        }

        // ---------------- Evaluacion offline (pura, sin red) ----------------
        public static LicenseStatus Evaluate(LicenseCache c, string fingerprint, DateTime now)
        {
            bool trialAvailable = c == null || !c.TrialUsed;
            if (c == null || string.IsNullOrEmpty(c.Key))
                return new LicenseStatus { State = LicenseState.Unlicensed, TrialAvailable = trialAvailable };

            var masked = Mask(c.Key);
            if (!string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return new LicenseStatus { State = LicenseState.Unlicensed, TrialAvailable = true, Message = "La licencia guardada pertenece a otro equipo." };
            if (c.Revoked)
                return new LicenseStatus { State = LicenseState.Revoked, MaskedKey = masked, Message = "Esta licencia se ha desactivado." };
            if (now < c.LastSeenUtc.AddHours(-1))
                return new LicenseStatus { State = LicenseState.ValidationRequired, MaskedKey = masked, TrialAvailable = trialAvailable,
                    Message = "El reloj del sistema va por detrás de la última vez que se usó TOJI." };

            if (c.IsTrial)
            {
                if (c.ExpiresAtUtc == null || c.ExpiresAtUtc <= now)
                    return new LicenseStatus { State = LicenseState.TrialExpired, MaskedKey = masked, ExpiresAtUtc = c.ExpiresAtUtc, Message = "Tu prueba gratuita ha terminado." };
                return new LicenseStatus
                {
                    State = LicenseState.Trial, MaskedKey = masked, ExpiresAtUtc = c.ExpiresAtUtc,
                    DaysLeft = (int)Math.Ceiling((c.ExpiresAtUtc.Value - now).TotalDays)
                };
            }

            if (c.ExpiresAtUtc != null && c.ExpiresAtUtc <= now)
                return new LicenseStatus { State = LicenseState.Unlicensed, MaskedKey = masked, TrialAvailable = trialAvailable, Message = "Tu licencia ha caducado." };
            if (now - c.LastValidatedUtc > LicenseConfig.OfflineGrace)
                return new LicenseStatus { State = LicenseState.ValidationRequired, MaskedKey = masked, TrialAvailable = trialAvailable,
                    Message = $"Hace {(int)(now - c.LastValidatedUtc).TotalDays} días que no se valida la licencia." };
            return new LicenseStatus { State = LicenseState.Licensed, MaskedKey = masked, ExpiresAtUtc = c.ExpiresAtUtc };
        }

        public static string Mask(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Length <= 4 ? "****" : "••••-" + key.Substring(key.Length - 4);
        }

        // ---------------- Operaciones online ----------------
        // Validacion en segundo plano al arrancar. Sin red se mantiene el estado offline.
        public async Task RefreshOnlineAsync(CancellationToken ct = default)
        {
            if (!HasKey) return;
            var r = await _provider.ValidateAsync(_cache.Key, Fingerprint, ct);
            ApplyValidation(r);
        }

        public async Task<(bool ok, string message)> ActivateAsync(string key, CancellationToken ct = default)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return (false, "Escribe tu clave de licencia.");
            var r = await _provider.ActivateAsync(key, Fingerprint, ct);
            if (r.Outcome != ProviderOutcome.Valid) return (false, Describe(r));
            var now = _utcNow();
            _cache = new LicenseCache
            {
                Key = r.Key ?? key, Fingerprint = Fingerprint, IsTrial = r.IsTrial, ExpiresAtUtc = r.ExpiresAtUtc,
                LastValidatedUtc = now, LastSeenUtc = now, TrialUsed = (_cache?.TrialUsed ?? false) || r.IsTrial
            };
            Save();
            return (Status.CanModify, Status.CanModify ? null : Describe(r));
        }

        public async Task<(bool ok, string message)> StartTrialAsync(CancellationToken ct = default)
        {
            var r = await _provider.StartTrialAsync(Fingerprint, ct);
            var now = _utcNow();
            if (r.Outcome == ProviderOutcome.TrialAlreadyUsed)
            {
                _cache ??= new LicenseCache { Fingerprint = Fingerprint, LastSeenUtc = now };
                _cache.TrialUsed = true;
                Save();
                return (false, Describe(r));
            }
            if (r.Outcome != ProviderOutcome.Valid) return (false, Describe(r));
            _cache = new LicenseCache
            {
                Key = r.Key, Fingerprint = Fingerprint, IsTrial = true, TrialUsed = true,
                ExpiresAtUtc = r.ExpiresAtUtc ?? now.AddDays(LicenseConfig.TrialDays), LastValidatedUtc = now, LastSeenUtc = now
            };
            Save();
            return (true, null);
        }

        private void ApplyValidation(ProviderResult r)
        {
            var now = _utcNow();
            switch (r.Outcome)
            {
                case ProviderOutcome.Valid:
                    _cache.LastValidatedUtc = now; _cache.Revoked = false;
                    _cache.IsTrial = r.IsTrial; _cache.ExpiresAtUtc = r.ExpiresAtUtc;
                    if (r.IsTrial) _cache.TrialUsed = true;
                    break;
                case ProviderOutcome.Expired:
                    _cache.ExpiresAtUtc = r.ExpiresAtUtc ?? now;
                    if (_cache.ExpiresAtUtc > now) _cache.ExpiresAtUtc = now;
                    break;
                case ProviderOutcome.Suspended:
                    _cache.Revoked = true;
                    break;
                case ProviderOutcome.NotFound:
                case ProviderOutcome.NotActivatedHere:
                case ProviderOutcome.TooManyMachines:
                    // La clave ya no vale en este PC (borrada o movida a otro equipo): se olvida, pero no la prueba usada
                    _cache = new LicenseCache { Fingerprint = Fingerprint, TrialUsed = _cache.TrialUsed || _cache.IsTrial, LastSeenUtc = _cache.LastSeenUtc };
                    break;
                default:
                    return;   // sin red / sin configurar: se mantiene el estado offline
            }
            Save();
        }

        public static string Describe(ProviderResult r) => r.Outcome switch
        {
            ProviderOutcome.Valid => null,
            ProviderOutcome.Expired => "Esta licencia ha caducado.",
            ProviderOutcome.Suspended => "Esta licencia está desactivada. Si crees que es un error, contacta con soporte.",
            ProviderOutcome.NotFound => "La clave no existe. Revisa que la has copiado completa.",
            ProviderOutcome.NotActivatedHere => "La licencia no está activada en este equipo.",
            ProviderOutcome.TooManyMachines => "Esta licencia ya está en uso en otro PC. Cada licencia vale para un equipo a la vez.",
            ProviderOutcome.TrialAlreadyUsed => "Este PC ya usó su prueba gratuita.",
            ProviderOutcome.NotConfigured => "Las licencias aún no están configuradas en esta versión de TOJI.",
            ProviderOutcome.NetworkError => "No hay conexión con el servidor de licencias. Revisa tu Internet e inténtalo de nuevo.",
            _ => string.IsNullOrEmpty(r.Message) ? "No se pudo completar la operación." : r.Message
        };

        private void Save()
        {
            if (_cache != null) _cache.LastSeenUtc = Max(_cache.LastSeenUtc, _utcNow());
            try { if (_cache != null) _store.Save(_cache); } catch { }
            Recompute();
        }

        private void Recompute()
        {
            var now = _utcNow();
            Status = Evaluate(_cache, Fingerprint, now);
            // Marca de "ultima vez visto" para detectar relojes atrasados (sin escribir en cada arranque)
            if (_cache != null && now > _cache.LastSeenUtc.AddHours(1))
            {
                _cache.LastSeenUtc = now;
                try { _store.Save(_cache); } catch { }
            }
            StatusChanged?.Invoke(Status);
        }

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }
}
