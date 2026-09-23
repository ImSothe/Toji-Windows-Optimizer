using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sothe.Tests
{
    // Bug 2: la OTA fusiona profiles.json sin borrar los perfiles del usuario, y solo acepta datos firmados
    public class OtaTests
    {
        private const string Url = "https://ota.test/sothe/";

        private sealed class FakeServer : HttpMessageHandler
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                var name = req.RequestUri.AbsolutePath.Split('/').Last();
                return Task.FromResult(Files.TryGetValue(name, out var b)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        // Publica en el servidor falso los JSON dados + manifest firmado, como haria tools/OtaSigner
        private static (FakeServer server, string publicKey) Publish(int version, Dictionary<string, byte[]> files, ECDsa key = null)
        {
            key ??= ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var server = new FakeServer();
            foreach (var f in files) server.Files[f.Key] = f.Value;
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new { version, files = files.ToDictionary(f => f.Key, f => Convert.ToHexString(SHA256.HashData(f.Value))) });
            server.Files["manifest.json"] = manifest;
            server.Files["manifest.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256)));
            return (server, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        private static byte[] OfficialProfiles(params string[] names) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            profiles = names.Select(n => new { id = n.ToLowerInvariant(), name = n, desc = "Oficial", tweaks = new[] { "game_mode" } })
        });

        [Fact]
        public async Task Actualizacion_FusionaPerfiles_YConservaLosDelUsuario()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            eng.SaveProfile("Mi setup", new List<string> { "game_mode" });
            var tweaks = File.ReadAllBytes(Path.Combine(d.Data, "tweaks.json"));
            var (server, pub) = Publish(5, new Dictionary<string, byte[]>
            {
                ["tweaks.json"] = tweaks,
                ["profiles.json"] = OfficialProfiles("Competitivo", "Ciberseguridad", "Limpio", "Streaming"),
            }, d.Key);

            var r = await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync();

            Assert.Equal(OtaStatus.Updated, r.Status);
            Assert.Equal(5, new DataUpdater(eng, Url, pub, server).LocalVersion());
            var all = eng.LoadProfiles();
            Assert.Contains(all, p => p.Id == "streaming" && !p.IsCustom);   // nuevo oficial
            Assert.Contains(all, p => p.Id == "mi_setup" && p.IsCustom);     // el del usuario sigue
        }

        [Fact]
        public async Task Actualizacion_RescataPerfilesAntiguosDentroDeProfilesJson()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            // Perfil propio que (por una version antigua) aun esta dentro del profiles.json oficial
            var official = Path.Combine(d.Data, "profiles.json");
            var doc = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(official), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            doc.Profiles.Add(new Profile { Id = "legacy", Name = "Legacy", Desc = SotheEngine.CustomProfileDesc, Tweaks = new List<string>() });
            File.WriteAllText(official, JsonSerializer.Serialize(doc));

            var (server, pub) = Publish(2, new Dictionary<string, byte[]> { ["profiles.json"] = OfficialProfiles("Competitivo") }, d.Key);
            var r = await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync();

            Assert.Equal(OtaStatus.Updated, r.Status);
            Assert.Contains(eng.LoadProfiles(), p => p.Id == "legacy" && p.IsCustom);
        }

        [Fact]
        public async Task FicheroManipulado_SeRechaza_YNoTocaNada()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var tweaksPath = Path.Combine(d.Data, "tweaks.json");
            var before = File.ReadAllText(tweaksPath);
            var (server, pub) = Publish(3, new Dictionary<string, byte[]> { ["tweaks.json"] = File.ReadAllBytes(tweaksPath) });
            server.Files["tweaks.json"] = Encoding.UTF8.GetBytes("{\"tweaks\":[{\"id\":\"malo\",\"ops\":[]}]}");   // cambiado tras firmar

            var r = await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync();

            Assert.Equal(OtaStatus.Failed, r.Status);
            Assert.Contains("SHA-256", r.Message);
            Assert.Equal(before, File.ReadAllText(tweaksPath));
        }

        [Fact]
        public async Task FirmaDeOtraClave_SeRechaza()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var (server, _) = Publish(3, new Dictionary<string, byte[]> { ["profiles.json"] = OfficialProfiles("X") });
            var otherKey = Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo());

            var r = await new DataUpdater(eng, Url, otherKey, server).CheckAndUpdateAsync();

            Assert.Equal(OtaStatus.Failed, r.Status);
            Assert.Contains("firma", r.Message);
        }

        [Fact]
        public async Task MismaVersion_NoDescargaNada()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var (server, pub) = Publish(1, new Dictionary<string, byte[]> { ["profiles.json"] = OfficialProfiles("Competitivo") });
            Assert.Equal(OtaStatus.Updated, (await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync()).Status);
            Assert.Equal(OtaStatus.UpToDate, (await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync()).Status);
        }

        [Fact]
        public async Task ActualizacionDescargada_GuardaLaFirma_YElMotorLaAcepta()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var (server, _) = Publish(4, new Dictionary<string, byte[]> { ["profiles.json"] = OfficialProfiles("Streaming") }, d.Key);

            Assert.Equal(OtaStatus.Updated, (await new DataUpdater(eng, Url, null, server).CheckAndUpdateAsync()).Status);

            Assert.True(File.Exists(Path.Combine(d.Data, "manifest.sig")));
            Assert.Contains(eng.LoadProfiles(), p => p.Id == "streaming" && !p.IsCustom);
        }

        [Fact]
        public async Task ManifiestoLocalSinFirma_NoBloqueaLasActualizaciones()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            // Un manifest.json puesto a mano con version enorme no debe hacer creer que ya esta al dia
            File.WriteAllText(Path.Combine(d.Data, "manifest.json"), "{ \"version\": 9999, \"files\": {} }");
            var (server, pub) = Publish(2, new Dictionary<string, byte[]> { ["profiles.json"] = OfficialProfiles("Competitivo") }, d.Key);

            Assert.Equal(0, new DataUpdater(eng, Url, pub, server).LocalVersion());
            Assert.Equal(OtaStatus.Updated, (await new DataUpdater(eng, Url, pub, server).CheckAndUpdateAsync()).Status);
        }

        [Fact]
        public async Task SinConfigurar_EstaDesactivada()
        {
            using var d = new TestDirs();
            Assert.Equal(OtaStatus.Disabled, (await new DataUpdater(d.Engine(), "", "").CheckAndUpdateAsync()).Status);
        }
    }
}
