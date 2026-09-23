using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Toji.Licensing;
using Xunit;

namespace Toji.Tests
{
    public class HardwareIdTests
    {
        [Fact]
        public void MismosComponentes_MismaHuella_SinImportarMayusculasNiEspacios() =>
            Assert.Equal(HardwareId.Compute("4c4c4544-0035-5a10-8047-b2c04f4d3732", " MB123XYZ ", null),
                         HardwareId.Compute("4C4C4544-0035-5A10-8047-B2C04F4D3732", "mb123xyz", null));

        [Fact]
        public void OtraPlaca_OtraHuella() =>
            Assert.NotEqual(HardwareId.Compute("4C4C4544-0035-5A10-8047-B2C04F4D3732", "MB123XYZ", null),
                            HardwareId.Compute("4C4C4544-0035-5A10-8047-B2C04F4D3732", "MB999ABC", null));

        [Theory]
        [InlineData("To be filled by O.E.M.")]
        [InlineData("Default string")]
        [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
        [InlineData("03000200-0400-0500-0006-000700080009")]
        [InlineData("0000000")]
        [InlineData("")]
        [InlineData(null)]
        public void ValoresDeRelleno_NoSeUsan(string v) => Assert.False(HardwareId.IsUsable(v));

        [Fact]
        public void SinUuidNiPlaca_UsaMachineGuid() =>
            Assert.Equal(64, HardwareId.Compute("To be filled by O.E.M.", "Default string", "6f1e2d3c-aaaa-4bbb-9ccc-1234567890ab").Length);

        [Fact]
        public void SinNingunComponente_Falla() =>
            Assert.Throws<InvalidOperationException>(() => HardwareId.Compute(null, "", "0"));

        [Fact]
        public void LaHuellaNoContieneLosNumerosDeSerie() =>
            Assert.DoesNotContain("MB123XYZ", HardwareId.Compute("4C4C4544-0035-5A10-8047-B2C04F4D3732", "MB123XYZ", null));
    }

    public class LicenseEvaluateTests
    {
        private const string Fp = "AABBCC";
        private static readonly DateTime Now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        private static LicenseCache Pro() => new LicenseCache { Key = "TOJI-KEY-1234", Fingerprint = Fp, LastValidatedUtc = Now.AddDays(-1), LastSeenUtc = Now.AddMinutes(-5) };

        [Fact] public void SinCache_SinLicencia_ConPruebaDisponible()
        {
            var s = LicenseService.Evaluate(null, Fp, Now);
            Assert.Equal(LicenseState.Unlicensed, s.State); Assert.True(s.TrialAvailable); Assert.False(s.CanModify);
        }

        [Fact] public void Permanente_Licenciada() => Assert.Equal(LicenseState.Licensed, LicenseService.Evaluate(Pro(), Fp, Now).State);

        [Fact] public void SinValidarMasDe14Dias_PideValidar()
        {
            var c = Pro(); c.LastValidatedUtc = Now.AddDays(-15);
            var s = LicenseService.Evaluate(c, Fp, Now);
            Assert.Equal(LicenseState.ValidationRequired, s.State); Assert.False(s.CanModify);
        }

        [Fact] public void RelojAtrasado_PideValidar()
        {
            var c = Pro(); c.LastSeenUtc = Now.AddDays(3);
            Assert.Equal(LicenseState.ValidationRequired, LicenseService.Evaluate(c, Fp, Now).State);
        }

        [Fact] public void LicenciaCopiadaDeOtroPC_NoVale() => Assert.Equal(LicenseState.Unlicensed, LicenseService.Evaluate(Pro(), "OTRO", Now).State);

        [Fact] public void Revocada() { var c = Pro(); c.Revoked = true; Assert.Equal(LicenseState.Revoked, LicenseService.Evaluate(c, Fp, Now).State); }

        [Fact] public void Prueba_DiasRestantes()
        {
            var c = Pro(); c.IsTrial = true; c.TrialUsed = true; c.ExpiresAtUtc = Now.AddDays(4.2);
            var s = LicenseService.Evaluate(c, Fp, Now);
            Assert.Equal(LicenseState.Trial, s.State); Assert.Equal(5, s.DaysLeft); Assert.True(s.CanModify);
        }

        [Fact] public void PruebaCaducada_SinNuevaPrueba()
        {
            var c = Pro(); c.IsTrial = true; c.TrialUsed = true; c.ExpiresAtUtc = Now.AddMinutes(-1);
            var s = LicenseService.Evaluate(c, Fp, Now);
            Assert.Equal(LicenseState.TrialExpired, s.State); Assert.False(s.TrialAvailable); Assert.False(s.CanModify);
        }
    }

    public class LicenseServiceTests
    {
        private sealed class PlainProtector : ISecretProtector
        {
            public byte[] Protect(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).ToArray();
            public byte[] Unprotect(byte[] d) => Protect(d);
        }

        private sealed class FakeProvider : ILicenseProvider
        {
            public ProviderResult Next = new ProviderResult { Outcome = ProviderOutcome.Valid };
            public ProviderResult Trial = new ProviderResult { Outcome = ProviderOutcome.Valid, IsTrial = true, Key = "TRIAL-KEY-0001" };
            public string Name => "Fake";
            public Task<ProviderResult> ActivateAsync(string key, string fp, CancellationToken ct = default) => Task.FromResult(Next);
            public Task<ProviderResult> ValidateAsync(string key, string fp, CancellationToken ct = default) => Task.FromResult(Next);
            public Task<ProviderResult> StartTrialAsync(string fp, CancellationToken ct = default) => Task.FromResult(Trial);
        }

        private static (LicenseService svc, FakeProvider prov, LicenseStore store, string dir) Make(DateTime? now = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "toji-lic-" + Guid.NewGuid().ToString("N"));
            var store = new LicenseStore(Path.Combine(dir, "license.dat"), new PlainProtector());
            var prov = new FakeProvider();
            var t = now ?? DateTime.UtcNow;
            return (new LicenseService(prov, store, "FP1", () => t), prov, store, dir);
        }

        [Fact]
        public async Task ActivarClaveValida_Licencia_YPersiste()
        {
            var (svc, prov, store, dir) = Make();
            var (ok, _) = await svc.ActivateAsync("TOJI-GOOD-9999");
            Assert.True(ok);
            Assert.Equal(LicenseState.Licensed, svc.Status.State);
            Assert.Equal("••••-9999", svc.Status.MaskedKey);
            var reopened = new LicenseService(prov, store, "FP1");   // siguiente arranque, sin red
            Assert.Equal(LicenseState.Licensed, reopened.Status.State);
            Assert.DoesNotContain("TOJI-GOOD-9999", File.ReadAllText(Path.Combine(dir, "license.dat")));   // no va en claro
        }

        [Fact]
        public async Task ClaveEnUsoEnOtroPC_NoActiva_YExplicaPorQue()
        {
            var (svc, prov, _, _) = Make();
            prov.Next = new ProviderResult { Outcome = ProviderOutcome.TooManyMachines };
            var (ok, msg) = await svc.ActivateAsync("TOJI-SHARED-0001");
            Assert.False(ok);
            Assert.Contains("otro PC", msg);
            Assert.Equal(LicenseState.Unlicensed, svc.Status.State);
        }

        [Fact]
        public async Task Prueba_UnaVezPorPC()
        {
            var (svc, prov, _, _) = Make();
            var first = await svc.StartTrialAsync();
            Assert.True(first.ok);
            Assert.Equal(LicenseState.Trial, svc.Status.State);
            Assert.Equal(LicenseConfig.TrialDays, svc.Status.DaysLeft);

            prov.Trial = new ProviderResult { Outcome = ProviderOutcome.TrialAlreadyUsed };
            var second = await svc.StartTrialAsync();
            Assert.False(second.ok);
            Assert.Contains("ya usó su prueba", second.message);
        }

        [Fact]
        public async Task RevocadaEnRemoto_SeBloquea()
        {
            var (svc, prov, _, _) = Make();
            await svc.ActivateAsync("TOJI-GOOD-9999");
            prov.Next = new ProviderResult { Outcome = ProviderOutcome.Suspended };
            await svc.RefreshOnlineAsync();
            Assert.Equal(LicenseState.Revoked, svc.Status.State);
            Assert.False(svc.Status.CanModify);
        }

        [Fact]
        public async Task SinRed_MantieneLaLicencia()
        {
            var (svc, prov, _, _) = Make();
            await svc.ActivateAsync("TOJI-GOOD-9999");
            prov.Next = new ProviderResult { Outcome = ProviderOutcome.NetworkError };
            await svc.RefreshOnlineAsync();
            Assert.Equal(LicenseState.Licensed, svc.Status.State);
        }

        [Fact]
        public async Task LicenciaMovidaAOtroPC_SeOlvidaAqui()
        {
            var (svc, prov, _, _) = Make();
            await svc.ActivateAsync("TOJI-GOOD-9999");
            prov.Next = new ProviderResult { Outcome = ProviderOutcome.NotActivatedHere };
            await svc.RefreshOnlineAsync();
            Assert.Equal(LicenseState.Unlicensed, svc.Status.State);
            Assert.False(svc.HasKey);
        }
    }

    // Cliente de Keygen contra respuestas con el formato de su API (validate-key + machines)
    public class KeygenProviderTests
    {
        private sealed class Fake : HttpMessageHandler
        {
            public readonly List<(string method, string path, string auth, string body)> Calls = new List<(string, string, string, string)>();
            public Func<HttpRequestMessage, string, HttpResponseMessage> Respond;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
                Calls.Add((r.Method.Method, r.RequestUri.AbsolutePath, r.Headers.Authorization?.ToString(), body));
                return Respond(r, body);
            }
        }

        private static HttpResponseMessage Json(int status, string json) =>
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json") };

        private static string Validation(bool valid, string code, string expiry = null, bool trial = false) =>
            "{\"meta\":{\"valid\":" + (valid ? "true" : "false") + ",\"code\":\"" + code + "\",\"detail\":\"" + code + "\"}," +
            "\"data\":{\"id\":\"lic-123\",\"type\":\"licenses\",\"attributes\":{\"expiry\":" + (expiry == null ? "null" : "\"" + expiry + "\"") +
            ",\"metadata\":{\"trial\":" + (trial ? "true" : "false") + "}}}}";

        [Fact]
        public async Task ValidaConHuella()
        {
            var h = new Fake { Respond = (r, b) => Json(200, Validation(true, "VALID")) };
            var r = await new KeygenLicenseProvider("acct", "https://w.test", h).ValidateAsync("KEY-1", "FP1");
            Assert.Equal(ProviderOutcome.Valid, r.Outcome);
            var call = Assert.Single(h.Calls);
            Assert.Equal("/v1/accounts/acct/licenses/actions/validate-key", call.path);
            Assert.Contains("\"fingerprint\":\"FP1\"", call.body);
        }

        [Theory]
        [InlineData("EXPIRED", ProviderOutcome.Expired)]
        [InlineData("SUSPENDED", ProviderOutcome.Suspended)]
        [InlineData("NOT_FOUND", ProviderOutcome.NotFound)]
        [InlineData("TOO_MANY_MACHINES", ProviderOutcome.TooManyMachines)]
        [InlineData("FINGERPRINT_SCOPE_MISMATCH", ProviderOutcome.NotActivatedHere)]
        public async Task CodigosDeKeygen(string code, ProviderOutcome expected)
        {
            var h = new Fake { Respond = (r, b) => Json(200, Validation(false, code)) };
            Assert.Equal(expected, (await new KeygenLicenseProvider("acct", "https://w.test", h).ValidateAsync("KEY-1", "FP1")).Outcome);
        }

        [Fact]
        public async Task Activar_CreaLaMaquinaConLaClave_YRevalida()
        {
            int validations = 0;
            var h = new Fake();
            h.Respond = (r, b) => r.RequestUri.AbsolutePath.EndsWith("/machines")
                ? Json(201, "{\"data\":{\"id\":\"m-1\",\"type\":\"machines\"}}")
                : Json(200, ++validations == 1 ? Validation(false, "NO_MACHINE") : Validation(true, "VALID"));
            var r = await new KeygenLicenseProvider("acct", "https://w.test", h).ActivateAsync("KEY-1", "FP1");
            Assert.Equal(ProviderOutcome.Valid, r.Outcome);
            var machine = Assert.Single(h.Calls, c => c.path.EndsWith("/machines"));
            Assert.Equal("License KEY-1", machine.auth);
            Assert.Contains("\"fingerprint\":\"FP1\"", machine.body);
            Assert.Contains("\"id\":\"lic-123\"", machine.body);
        }

        [Fact]
        public async Task Activar_LimiteDeMaquinas()
        {
            var h = new Fake();
            h.Respond = (r, b) => r.RequestUri.AbsolutePath.EndsWith("/machines")
                ? Json(422, "{\"errors\":[{\"code\":\"MACHINE_LIMIT_EXCEEDED\"}]}")
                : Json(200, Validation(false, "NO_MACHINE"));
            Assert.Equal(ProviderOutcome.TooManyMachines, (await new KeygenLicenseProvider("acct", "https://w.test", h).ActivateAsync("KEY-1", "FP1")).Outcome);
        }

        [Fact]
        public async Task Prueba_ViaWorker()
        {
            var h = new Fake { Respond = (r, b) => Json(200, "{\"key\":\"TRIAL-ABC\",\"expiry\":\"2026-09-30T12:00:00Z\"}") };
            var r = await new KeygenLicenseProvider("acct", "https://w.test/", h).StartTrialAsync("FP1");
            Assert.Equal(ProviderOutcome.Valid, r.Outcome);
            Assert.True(r.IsTrial);
            Assert.Equal("TRIAL-ABC", r.Key);
            Assert.Equal(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), r.ExpiresAtUtc);
            Assert.Equal("/trial", h.Calls.Single().path);
        }

        [Fact]
        public async Task Prueba_YaUsada()
        {
            var h = new Fake { Respond = (r, b) => Json(409, "{\"error\":\"TRIAL_USED\"}") };
            Assert.Equal(ProviderOutcome.TrialAlreadyUsed, (await new KeygenLicenseProvider("acct", "https://w.test", h).StartTrialAsync("FP1")).Outcome);
        }

        [Fact]
        public async Task SinRed_EsErrorDeRed()
        {
            var h = new Fake { Respond = (r, b) => throw new HttpRequestException("sin conexión") };
            Assert.Equal(ProviderOutcome.NetworkError, (await new KeygenLicenseProvider("acct", "https://w.test", h).ValidateAsync("KEY-1", "FP1")).Outcome);
        }
    }
}
