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
using Sothe;
using Xunit;

namespace Sothe.Tests
{
    // Auto-actualizacion: la app NUNCA prepara ni instala un binario sin verificar antes la firma del autor y el
    // SHA-256. Estos tests cubren la comprobacion contra un GitHub simulado con la forma real de su API de releases.
    public class AppUpdaterTests
    {
        private const string Repo = "ImSothe/Toji-Windows-Optimizer";
        private static readonly Version Current = new Version(1, 0, 0);

        // GitHub simulado: sirve /repos/{repo}/releases/latest y las URLs de descarga de los assets
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public readonly Dictionary<string, byte[]> Assets = new Dictionary<string, byte[]>();   // nombre -> bytes
            public string Tag = "v1.1.0";
            public int Calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                Calls++;
                var path = req.RequestUri.AbsolutePath;
                if (path.EndsWith("/releases/latest"))
                {
                    var assets = string.Join(",", Assets.Keys.Select(n =>
                        $"{{\"name\":\"{n}\",\"browser_download_url\":\"https://dl.test/{n}\"}}"));
                    return Text(200, $"{{\"tag_name\":\"{Tag}\",\"assets\":[{assets}]}}");
                }
                var name = path.TrimStart('/');
                return Assets.TryGetValue(name, out var b)
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) })
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Text(int s, string json) =>
                Task.FromResult(new HttpResponseMessage((HttpStatusCode)s) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        // Publica una release firmada (release.json + release.sig + exe), como haria "OtaSigner sign-app"
        private static (FakeGitHub gh, string pub, byte[] exe) Publish(string version, ECDsa key = null, byte[] exeBytes = null, byte[] tamperExe = null)
        {
            key ??= ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var exe = exeBytes ?? Encoding.ASCII.GetBytes("MZ fake exe " + version);
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version, file = "SotheOptimizer.exe",
                sha256 = Convert.ToHexString(SHA256.HashData(exe)), notes = "notas " + version
            });
            var gh = new FakeGitHub { Tag = "v" + version };
            gh.Assets["release.json"] = manifest;
            gh.Assets["release.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256)));
            gh.Assets["SotheOptimizer.exe"] = tamperExe ?? exe;   // tamperExe: el exe servido no coincide con el firmado
            return (gh, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), exe);
        }

        private static AppUpdater Make(FakeGitHub gh, string pub, Version current = null) =>
            new AppUpdater(current ?? Current, Repo, pub, gh, "https://api.test", new[] { "api.test", "dl.test" });

        [Fact]
        public async Task VersionNueva_Firmada_SeOfrece()
        {
            var (gh, pub, exe) = Publish("1.2.0");
            var info = await Make(gh, pub).CheckAsync();
            Assert.Equal(UpdateStatus.Available, info.Status);
            Assert.Equal(new Version(1, 2, 0), info.Version);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(exe)), info.Sha256);
            Assert.Equal("https://dl.test/SotheOptimizer.exe", info.ExeUrl);
        }

        [Fact]
        public async Task MismaOMenorVersion_NoSeActualiza()
        {
            var (gh, pub, _) = Publish("1.0.0");
            Assert.Equal(UpdateStatus.UpToDate, (await Make(gh, pub).CheckAsync()).Status);
            var (gh2, pub2, _) = Publish("0.9.0");
            Assert.Equal(UpdateStatus.UpToDate, (await Make(gh2, pub2).CheckAsync()).Status);
        }

        [Fact]
        public async Task ManifiestoManipuladoTrasFirmar_SeRechaza()
        {
            var (gh, pub, _) = Publish("1.2.0");
            // Se cambia release.json despues de firmarlo (p. ej. apuntando a otro binario)
            gh.Assets["release.json"] = Encoding.UTF8.GetBytes("{\"version\":\"9.9.9\",\"file\":\"SotheOptimizer.exe\",\"sha256\":\"00\"}");
            var info = await Make(gh, pub).CheckAsync();
            Assert.Equal(UpdateStatus.Failed, info.Status);
            Assert.Contains("signature", info.Message);
        }

        [Fact]
        public async Task FirmadaConOtraClave_SeRechaza()
        {
            var (gh, _, _) = Publish("1.2.0");   // firmada con una clave...
            var otra = Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo());
            Assert.Equal(UpdateStatus.Failed, (await Make(gh, otra).CheckAsync()).Status);   // ...verificada con otra
        }

        [Fact]
        public async Task SinConfigurar_EstaDesactivada_YNoLlamaARed()
        {
            var (gh, _, _) = Publish("1.2.0");
            var updater = new AppUpdater(Current, "", "clave", gh, "https://api.test");
            Assert.False(updater.IsConfigured);
            Assert.Equal(UpdateStatus.Disabled, (await updater.CheckAsync()).Status);
            Assert.Equal(0, gh.Calls);
        }

        [Fact]
        public async Task Descarga_HashCorrecto_QuedaPreparada()
        {
            var (gh, pub, exe) = Publish("1.2.0");
            var updater = Make(gh, pub);
            var stage = NewDir();
            var staged = await updater.DownloadAndStageAsync(await updater.CheckAsync(), stage);

            Assert.Equal(UpdateStatus.Staged, staged.Status);
            Assert.Equal(new Version(1, 2, 0), staged.Version);
            Assert.True(File.Exists(Path.Combine(stage, AppUpdater.PendingExe)));
            Assert.Equal(exe, File.ReadAllBytes(Path.Combine(stage, AppUpdater.PendingExe)));
            Assert.True(File.Exists(Path.Combine(stage, AppUpdater.PendingInfo)));
        }

        [Fact]
        public async Task Descarga_BinarioQueNoCoincideConLaFirma_SeDescarta()
        {
            // La firma cubre 'exe' pero GitHub sirve otros bytes (release secuestrada / MITM)
            var real = Encoding.ASCII.GetBytes("MZ bueno");
            var (gh, pub, _) = Publish("1.2.0", exeBytes: real, tamperExe: Encoding.ASCII.GetBytes("MZ MALICIOSO"));
            var updater = Make(gh, pub);
            var stage = NewDir();
            var staged = await updater.DownloadAndStageAsync(await updater.CheckAsync(), stage);

            Assert.Equal(UpdateStatus.Failed, staged.Status);
            Assert.False(File.Exists(Path.Combine(stage, AppUpdater.PendingExe)));   // no se guarda nada
        }

        [Fact]
        public void TryApplyStaged_PreparadoInvalido_SeDescarta_YNoSeEjecuta()
        {
            var stage = NewDir();
            File.WriteAllBytes(Path.Combine(stage, AppUpdater.PendingExe), Encoding.ASCII.GetBytes("bin"));
            // pending.json con un SHA-256 que NO es el del fichero: preparado corrupto
            File.WriteAllText(Path.Combine(stage, AppUpdater.PendingInfo), "{\"version\":\"2.0.0\",\"sha256\":\"DEADBEEF\"}");

            Assert.False(AppUpdater.TryApplyStaged("/ruta/SotheOptimizer.exe", stage, Current, out _));
            Assert.False(File.Exists(Path.Combine(stage, AppUpdater.PendingExe)));   // descartado
        }

        [Fact]
        public void TryApplyStaged_VersionNoMasNueva_SeDescarta()
        {
            var stage = NewDir();
            var bin = Encoding.ASCII.GetBytes("bin");
            File.WriteAllBytes(Path.Combine(stage, AppUpdater.PendingExe), bin);
            File.WriteAllText(Path.Combine(stage, AppUpdater.PendingInfo),
                $"{{\"version\":\"1.0.0\",\"sha256\":\"{Convert.ToHexString(SHA256.HashData(bin))}\"}}");

            Assert.False(AppUpdater.TryApplyStaged("/ruta/SotheOptimizer.exe", stage, Current, out _));
            Assert.False(File.Exists(Path.Combine(stage, AppUpdater.PendingExe)));
        }

        [Fact]
        public void TryApplyStaged_PreparadoValido_NoSeDescarta()
        {
            // Preparado correcto y mas nuevo: en Windows se aplicaria; aqui (Linux) TryApplyStaged devuelve false por
            // plataforma pero NO debe borrar el binario, para poder aplicarlo en el arranque real
            var stage = NewDir();
            var bin = Encoding.ASCII.GetBytes("bin nuevo");
            File.WriteAllBytes(Path.Combine(stage, AppUpdater.PendingExe), bin);
            File.WriteAllText(Path.Combine(stage, AppUpdater.PendingInfo),
                $"{{\"version\":\"2.0.0\",\"sha256\":\"{Convert.ToHexString(SHA256.HashData(bin))}\"}}");

            AppUpdater.TryApplyStaged("/ruta/SotheOptimizer.exe", stage, Current, out _);
            // En Linux (donde corren los tests) no se aplica por plataforma, pero el preparado valido se conserva
            if (!OperatingSystem.IsWindows())
                Assert.True(File.Exists(Path.Combine(stage, AppUpdater.PendingExe)));
        }

        [Fact]
        public void SinPreparado_NoHaceNada()
        {
            Assert.False(AppUpdater.TryApplyStaged("/ruta/SotheOptimizer.exe", NewDir(), Current, out _));
        }

        [Fact]
        public void ScriptDeReemplazo_EsperaAlProceso_MueveYRelanza()
        {
            var s = AppUpdater.BuildSwapScript(4321, @"C:\stage\SotheOptimizer.pending.exe", @"C:\app\SotheOptimizer.exe", @"C:\stage\pending.json");
            Assert.Contains("PID eq 4321", s);
            int wait = s.IndexOf("PID eq 4321", StringComparison.Ordinal);
            int move = s.IndexOf("move /y", StringComparison.Ordinal);
            int start = s.IndexOf("start \"\"", StringComparison.Ordinal);
            Assert.True(wait < move && move < start, "debe esperar, luego mover, luego relanzar");
            Assert.Contains(@"move /y ""C:\stage\SotheOptimizer.pending.exe"" ""C:\app\SotheOptimizer.exe""", s);
            Assert.Contains(@"start """" ""C:\app\SotheOptimizer.exe""", s);
        }

        private static string NewDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "sothe-upd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
