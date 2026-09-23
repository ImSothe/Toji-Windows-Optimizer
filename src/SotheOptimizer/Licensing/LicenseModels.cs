using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sothe.Licensing
{
    public enum LicenseState
    {
        Unlicensed,          // sin licencia: se ve todo, no se aplica
        Trial,               // prueba gratuita en curso
        TrialExpired,        // prueba terminada
        Licensed,            // licencia permanente valida
        Revoked,             // suspendida/revocada en remoto (reembolso, clave compartida...)
        ValidationRequired   // sin validar online demasiado tiempo o reloj atrasado
    }

    public sealed class LicenseStatus
    {
        public LicenseState State { get; init; }
        public int DaysLeft { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public string MaskedKey { get; init; }
        public string Message { get; init; }
        public bool TrialAvailable { get; init; }
        public bool CanModify => State == LicenseState.Licensed || State == LicenseState.Trial;
    }

    // Lo que se guarda cifrado en %ProgramData%\SotheOptimizer\license.dat
    public sealed class LicenseCache
    {
        public string Key { get; set; }
        public string Fingerprint { get; set; }
        public bool IsTrial { get; set; }
        public bool Revoked { get; set; }
        public bool TrialUsed { get; set; }            // este PC ya consumio su prueba
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime LastValidatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }       // para detectar un reloj atrasado
        public string PendingNotice { get; set; }       // aviso pendiente de mostrar (licencia perdida en este PC)
        public string NoticeShown { get; set; }         // ultimo bloqueo ya avisado: cada caducidad se avisa una sola vez
    }

    public enum ProviderOutcome { Valid, Expired, Suspended, NotFound, WrongProduct, NotActivatedHere, TooManyMachines, TrialAlreadyUsed, NotConfigured, NetworkError, Error }

    public sealed class ProviderResult
    {
        public ProviderOutcome Outcome { get; init; }
        public string Key { get; init; }
        public bool IsTrial { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public string Message { get; init; }
    }

    // Backend de licencias (Keygen en produccion, Demo en compilaciones Debug)
    public interface ILicenseProvider
    {
        string Name { get; }
        bool SupportsSelfTrial { get; }   // prueba de un clic sin clave (Worker); si no, la prueba es una clave
        Task<ProviderResult> ActivateAsync(string key, string fingerprint, CancellationToken ct = default);
        Task<ProviderResult> ValidateAsync(string key, string fingerprint, CancellationToken ct = default);
        Task<ProviderResult> StartTrialAsync(string fingerprint, CancellationToken ct = default);
    }

    public interface ISecretProtector
    {
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);
    }
}
