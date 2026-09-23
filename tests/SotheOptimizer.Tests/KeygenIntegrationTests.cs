using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sothe.Licensing;
using Xunit;

namespace Sothe.Tests
{
    // Flujo completo LicenseService + KeygenLicenseProvider + cache cifrada contra un Keygen simulado con estado, con
    // las reglas de la politica real: producto, caducidad, suspension, 1 maquina por clave (node-locked) y una prueba por PC
    public class KeygenIntegrationTests
    {
        private const string TrialPolicy = LicenseConfig.KeygenTrialPolicyId;
        private const string ProPolicy = "politica-pro";

        private sealed class FakeKeygen : HttpMessageHandler
        {
            public sealed class License
            {
                public string Id = Guid.NewGuid().ToString(), Key, Policy = TrialPolicy, Product = LicenseConfig.KeygenProductId;
                public DateTime? Expiry;
                public bool Suspended;
                public readonly List<string> Machines = new List<string>();
            }

            public readonly List<License> Licenses = new List<License>();
            public readonly List<string> Paths = new List<string>();
            public DateTime Now = DateTime.UtcNow;
            public bool Offline;

            public License Add(string key, string policy = TrialPolicy, DateTime? expiry = null)
            {
                var l = new License { Key = key, Policy = policy, Expiry = expiry };
                Licenses.Add(l);
                return l;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                if (Offline) throw new HttpRequestException("sin conexión");
                Paths.Add(req.RequestUri.AbsolutePath);
                using var doc = JsonDocument.Parse(await req.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                var account = "/v1/accounts/" + LicenseConfig.KeygenAccountId + "/";
                if (req.RequestUri.AbsolutePath == account + "licenses/actions/validate-key") return Validate(root.GetProperty("meta"));
                if (req.RequestUri.AbsolutePath == account + "machines") return Activate(req, root.GetProperty("data"));
                return Json(404, "{\"errors\":[{\"code\":\"NOT_FOUND\"}]}");
            }

            private HttpResponseMessage Validate(JsonElement meta)
            {
                var key = meta.GetProperty("key").GetString();
                var scope = meta.GetProperty("scope");
                var fp = scope.GetProperty("fingerprint").GetString();
                var product = scope.TryGetProperty("product", out var p) ? p.GetString() : null;
                var l = Licenses.FirstOrDefault(x => x.Key == key);
                if (l == null) return Json(200, "{\"meta\":{\"valid\":false,\"code\":\"NOT_FOUND\",\"detail\":\"does not exist\"},\"data\":null}");
                string code =
                    product != null && product != l.Product ? "PRODUCT_SCOPE_MISMATCH" :
                    l.Suspended ? "SUSPENDED" :
                    l.Expiry != null && l.Expiry <= Now ? "EXPIRED" :
                    l.Machines.Count == 0 ? "NO_MACHINE" :
                    !l.Machines.Contains(fp) ? "FINGERPRINT_SCOPE_MISMATCH" : "VALID";
                var data = new
                {
                    id = l.Id, type = "licenses",
                    attributes = new { key = l.Key, expiry = l.Expiry?.ToString("o"), metadata = new { } },
                    relationships = new { policy = new { data = new { type = "policies", id = l.Policy } } }
                };
                return Json(200, JsonSerializer.Serialize(new { meta = new { valid = code == "VALID", code, detail = code }, data }));
            }

            private HttpResponseMessage Activate(HttpRequestMessage req, JsonElement data)
            {
                var id = data.GetProperty("relationships").GetProperty("license").GetProperty("data").GetProperty("id").GetString();
                var fp = data.GetProperty("attributes").GetProperty("fingerprint").GetString();
                var l = Licenses.FirstOrDefault(x => x.Id == id);
                // Autenticacion con la propia clave (politica con Authentication strategy = LICENSE)
                if (l == null || req.Headers.Authorization?.Scheme != "License" || req.Headers.Authorization.Parameter != l.Key)
                    return Json(401, "{\"errors\":[{\"code\":\"TOKEN_INVALID\"}]}");
                if (Licenses.Any(x => x != l && x.Policy == l.Policy && x.Machines.Contains(fp)))
                    return Json(422, "{\"errors\":[{\"title\":\"Unprocessable resource\",\"code\":\"FINGERPRINT_TAKEN\"}]}");
                if (l.Machines.Count >= 1)
                    return Json(422, "{\"errors\":[{\"title\":\"Unprocessable resource\",\"code\":\"MACHINE_LIMIT_EXCEEDED\"}]}");
                l.Machines.Add(fp);
                return Json(201, "{\"data\":{\"id\":\"m-" + l.Machines.Count + "\",\"type\":\"machines\"}}");
            }

            private static HttpResponseMessage Json(int status, string json) =>
                new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json") };
        }

        private sealed class PlainProtector : ISecretProtector
        {
            public byte[] Protect(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).ToArray();
            public byte[] Unprotect(byte[] d) => Protect(d);
        }

        // Un "PC": su huella y su license.dat. Open() = arrancar la app en ese PC en el instante 'now'
        private sealed class Pc
        {
            private readonly FakeKeygen _server;
            private readonly LicenseStore _store;
            public readonly string Fingerprint;
            public Pc(FakeKeygen server, string fingerprint)
            {
                _server = server;
                Fingerprint = fingerprint;
                _store = new LicenseStore(Path.Combine(Path.GetTempPath(), "sothe-kg-" + Guid.NewGuid().ToString("N"), "license.dat"), new PlainProtector());
            }
            public LicenseService Open(DateTime now) => new LicenseService(
                new KeygenLicenseProvider(LicenseConfig.KeygenAccountId, LicenseConfig.KeygenProductId, LicenseConfig.KeygenTrialPolicyId, "", _server),
                _store, Fingerprint, () => now);
        }

        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task ClaveDePrueba3Meses_SeActivaEnEstePC_YQuedaEnPrueba()
        {
            var server = new FakeKeygen { Now = T0 };
            var lic = server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");

            var svc = pc.Open(T0);
            var (ok, msg) = await svc.ActivateAsync("  PRUEBA-AAAA-1111  ");

            Assert.True(ok, msg);
            Assert.Equal(LicenseState.Trial, svc.Status.State);
            Assert.True(svc.Status.CanModify);
            Assert.InRange(svc.Status.DaysLeft, 90, 92);
            Assert.Equal(new[] { "FP-PC1" }, lic.Machines);   // node-locked a este PC
            // Siguiente arranque sin red: sigue en prueba desde la cache
            server.Offline = true;
            var restarted = pc.Open(T0.AddDays(10));
            await restarted.RefreshOnlineAsync();
            Assert.Equal(LicenseState.Trial, restarted.Status.State);
            Assert.InRange(restarted.Status.DaysLeft, 80, 82);
        }

        [Fact]
        public async Task PeticionesUsanLaCuentaYElProductoOficiales()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            await new Pc(server, "FP-PC1").Open(T0).ActivateAsync("PRUEBA-AAAA-1111");
            Assert.All(server.Paths, path => Assert.StartsWith("/v1/accounts/00000000-0000-0000-0000-000000000000/", path));
            Assert.Contains(server.Paths, p => p.EndsWith("/machines"));
        }

        [Fact]
        public async Task NodeLocked_LaMismaClaveEnOtroPC_SeRechaza()
        {
            var server = new FakeKeygen { Now = T0 };
            var lic = server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            Assert.True((await new Pc(server, "FP-PC1").Open(T0).ActivateAsync("PRUEBA-AAAA-1111")).ok);

            var other = new Pc(server, "FP-PC2").Open(T0);
            var (ok, msg) = await other.ActivateAsync("PRUEBA-AAAA-1111");

            Assert.False(ok);
            Assert.Contains("otro PC", msg);
            Assert.False(other.Status.CanModify);
            Assert.Equal(new[] { "FP-PC1" }, lic.Machines);
        }

        [Fact]
        public async Task UnaSolaClaveDePruebaPorPC()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            server.Add("PRUEBA-BBBB-2222", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");
            Assert.True((await pc.Open(T0).ActivateAsync("PRUEBA-AAAA-1111")).ok);

            var (ok, msg) = await pc.Open(T0).ActivateAsync("PRUEBA-BBBB-2222");

            Assert.False(ok);
            Assert.Contains("ya usó su prueba", msg);
        }

        [Fact]
        public async Task LicenciaDeOtraPolitica_EsPro()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRO-CCCC-3333", ProPolicy);
            var svc = new Pc(server, "FP-PC1").Open(T0);
            Assert.True((await svc.ActivateAsync("PRO-CCCC-3333")).ok);
            Assert.Equal(LicenseState.Licensed, svc.Status.State);
        }

        [Theory]
        [InlineData("NO-EXISTE-0000", "no existe")]
        [InlineData("OTRO-PRODUCTO-1", "no es de Sothe Optimizer")]
        [InlineData("CADUCADA-0000", "caducado")]
        [InlineData("SUSPENDIDA-000", "desactivada")]
        public async Task ClavesNoValidas_NoActivan_YExplicanPorQue(string key, string reason)
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("OTRO-PRODUCTO-1").Product = "otro-producto";
            server.Add("CADUCADA-0000", TrialPolicy, T0.AddDays(-1));
            server.Add("SUSPENDIDA-000", ProPolicy).Suspended = true;
            var svc = new Pc(server, "FP-PC1").Open(T0);

            var (ok, msg) = await svc.ActivateAsync(key);

            Assert.False(ok);
            Assert.Contains(reason, msg);
            Assert.False(svc.Status.CanModify);
        }

        [Fact]
        public async Task AlArrancar_PruebaCaducada_BloqueaPro_YAvisaUnaSolaVez()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");
            await pc.Open(T0).ActivateAsync("PRUEBA-AAAA-1111");

            // Cuatro meses despues: el servidor responde EXPIRED
            server.Now = T0.AddMonths(4);
            var svc = pc.Open(T0.AddMonths(4));
            await svc.RefreshOnlineAsync();

            Assert.Equal(LicenseState.TrialExpired, svc.Status.State);
            Assert.False(svc.Status.CanModify);
            var notice = svc.TakeNotice();
            Assert.Contains("prueba", notice);
            Assert.Contains(LicenseService.FreeStillWorks, notice);
            Assert.Null(svc.TakeNotice());                              // no se repite en esta sesion
            Assert.Null(pc.Open(T0.AddMonths(4).AddDays(1)).TakeNotice());   // ni en el siguiente arranque
        }

        [Fact]
        public async Task AlArrancar_SinRedYPruebaCaducada_TambienBloquea()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");
            await pc.Open(T0).ActivateAsync("PRUEBA-AAAA-1111");

            server.Offline = true;
            var svc = pc.Open(T0.AddMonths(3).AddDays(1));
            await svc.RefreshOnlineAsync();

            Assert.Equal(LicenseState.TrialExpired, svc.Status.State);
            Assert.NotNull(svc.TakeNotice());
        }

        [Fact]
        public async Task AlArrancar_ClaveSuspendidaEnKeygen_SeBloquea()
        {
            var server = new FakeKeygen { Now = T0 };
            var lic = server.Add("PRO-CCCC-3333", ProPolicy);
            var pc = new Pc(server, "FP-PC1");
            await pc.Open(T0).ActivateAsync("PRO-CCCC-3333");

            lic.Suspended = true;   // reembolso o clave compartida
            var svc = pc.Open(T0.AddDays(1));
            await svc.RefreshOnlineAsync();

            Assert.Equal(LicenseState.Revoked, svc.Status.State);
            Assert.False(svc.Status.CanModify);
            Assert.Contains("desactivado", svc.TakeNotice());
        }

        [Fact]
        public async Task AlArrancar_MaquinaDesactivadaEnKeygen_SeOlvidaLaClave_YAvisa()
        {
            var server = new FakeKeygen { Now = T0 };
            var lic = server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");
            await pc.Open(T0).ActivateAsync("PRUEBA-AAAA-1111");

            lic.Machines.Clear();   // el autor libera la maquina desde el panel de Keygen
            var svc = pc.Open(T0.AddDays(1));
            await svc.RefreshOnlineAsync();

            Assert.Equal(LicenseState.Unlicensed, svc.Status.State);
            Assert.False(svc.HasKey);
            Assert.Contains("ya no está activa en este PC", svc.TakeNotice());
            Assert.Null(pc.Open(T0.AddDays(1)).TakeNotice());
        }

        [Fact]
        public async Task AlArrancar_SinRed_MantieneLaPrueba()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var pc = new Pc(server, "FP-PC1");
            await pc.Open(T0).ActivateAsync("PRUEBA-AAAA-1111");

            server.Offline = true;
            var svc = pc.Open(T0.AddDays(30));
            await svc.RefreshOnlineAsync();

            Assert.Equal(LicenseState.Trial, svc.Status.State);
            Assert.Null(svc.TakeNotice());
        }

        [Fact]
        public async Task ActivarSinPermisoDeClaveEnLaPolitica_DaUnErrorClaro()
        {
            var server = new FakeKeygen { Now = T0 };
            server.Add("PRUEBA-AAAA-1111", TrialPolicy, T0.AddMonths(3));
            var provider = new KeygenLicenseProvider(LicenseConfig.KeygenAccountId, LicenseConfig.KeygenProductId, TrialPolicy, "", new RejectMachines(server));

            var r = await provider.ActivateAsync("PRUEBA-AAAA-1111", "FP-PC1");

            Assert.Equal(ProviderOutcome.Error, r.Outcome);
            Assert.Contains("403", r.Message);
        }

        // Keygen con la politica en Authentication strategy = TOKEN: rechaza activar maquinas con la clave
        private sealed class RejectMachines : DelegatingHandler
        {
            public RejectMachines(HttpMessageHandler inner) : base(inner) { }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
                req.RequestUri.AbsolutePath.EndsWith("/machines")
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"errors\":[{\"code\":\"FORBIDDEN\"}]}") })
                    : base.SendAsync(req, ct);
        }
    }

    // Pruebas contra api.keygen.sh de verdad. Solo corren con SOTHE_KEYGEN_LIVE=1 (necesitan Internet) y no activan
    // maquinas, asi que no gastan el unico PC de ninguna clave. Opcional: SOTHE_KEYGEN_TEST_KEY=<una clave de prueba>
    public sealed class KeygenLiveFactAttribute : FactAttribute
    {
        public KeygenLiveFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("SOTHE_KEYGEN_LIVE") != "1")
                Skip = "Pruebas contra api.keygen.sh: ejecuta con SOTHE_KEYGEN_LIVE=1";
        }
    }

    public class KeygenLiveTests
    {
        private static KeygenLicenseProvider Real() =>
            new KeygenLicenseProvider(LicenseConfig.KeygenAccountId, LicenseConfig.KeygenProductId, LicenseConfig.KeygenTrialPolicyId);

        [KeygenLiveFact]
        public async Task CuentaReal_ClaveInventada_NoExiste()
        {
            var r = await Real().ValidateAsync("SOTHE-CLAVE-INVENTADA-" + Guid.NewGuid().ToString("N"), "sothe-test-fingerprint");
            Assert.Equal(ProviderOutcome.NotFound, r.Outcome);
        }

        [KeygenLiveFact]
        public async Task CuentaReal_ClaveDePrueba_EsDeEsteProductoYDePrueba()
        {
            var key = Environment.GetEnvironmentVariable("SOTHE_KEYGEN_TEST_KEY");
            if (string.IsNullOrWhiteSpace(key)) return;   // sin clave de prueba solo se comprueba la cuenta (test anterior)
            // Huella inventada: la clave existe y es del producto, pero no esta activada en "este" PC
            var r = await Real().ValidateAsync(key, "sothe-test-" + Guid.NewGuid().ToString("N"));
            Assert.Contains(r.Outcome, new[] { ProviderOutcome.NotActivatedHere, ProviderOutcome.Expired });
            Assert.True(r.IsTrial, "La clave no pertenece a la politica de prueba configurada en LicenseConfig.KeygenTrialPolicyId");
        }
    }
}
