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
    // Endurecimiento de la auto-actualizacion: solo HTTPS de GitHub, tope de tamano, errores de datos mal formados
    // sin excepciones y script de reemplazo con copia de seguridad, tiempo maximo y escape de rutas.
    public class AppUpdaterHardeningTests
    {
        private const string Repo = "ImSothe/Toji-Windows-Optimizer";

        private sealed class Gh : HttpMessageHandler
        {
            public readonly Dictionary<string, byte[]> Assets = new Dictionary<string, byte[]>();
            public string ExeUrl = "https://github.com/dl/SotheOptimizer.exe";
            public string SigUrl = "https://github.com/dl/release.sig";
            public byte[] ExeBody;
            public long? ExeDeclaredLength;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                var url = req.RequestUri.ToString();
                if (req.RequestUri.AbsolutePath.EndsWith("/releases/latest"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"assets\":[" +
                            "{\"name\":\"release.json\",\"browser_download_url\":\"https://github.com/dl/release.json\"}," +
                            $"{{\"name\":\"release.sig\",\"browser_download_url\":\"{SigUrl}\"}}," +
                            $"{{\"name\":\"SotheOptimizer.exe\",\"browser_download_url\":\"{ExeUrl}\"}}]}}", Encoding.UTF8, "application/json")
                    });
                if (url.EndsWith("release.json")) return Ok(Assets["release.json"]);
                if (url.EndsWith("release.sig")) return Ok(Assets["release.sig"]);
                if (url.EndsWith("SotheOptimizer.exe"))
                {
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ExeBody) };
                    if (ExeDeclaredLength != null) r.Content.Headers.ContentLength = ExeDeclaredLength;
                    return Task.FromResult(r);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Ok(byte[] b) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) });
        }

        private static (Gh gh, string pub, byte[] exe) Publish(string version, long? size = null, byte[] sigOverride = null)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var exe = Encoding.ASCII.GetBytes("MZ exe " + version);
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version, file = "SotheOptimizer.exe", sha256 = Convert.ToHexString(SHA256.HashData(exe)), size = size ?? exe.LongLength
            });
            var gh = new Gh { ExeBody = exe };
            gh.Assets["release.json"] = manifest;
            gh.Assets["release.sig"] = sigOverride ?? Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256)));
            return (gh, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), exe);
        }

        private static AppUpdater Make(Gh gh, string pub, string apiBase = "https://api.github.com") =>
            new AppUpdater(new Version(1, 0, 0), Repo, pub, gh, apiBase);

        private static string NewDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "sothe-upd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Theory]
        [InlineData("https://github.com/x", true)]
        [InlineData("https://api.github.com/x", true)]
        [InlineData("https://objects.githubusercontent.com/x", true)]
        [InlineData("https://release-assets.githubusercontent.com/x", true)]
        [InlineData("http://github.com/x", false)]                       // sin cifrar
        [InlineData("https://evil.example.com/x", false)]                // host ajeno
        [InlineData("https://github.com.evil.com/x", false)]             // truco de subdominio
        [InlineData("https://notgithubusercontent.com/x", false)]        // sufijo sin punto
        [InlineData("file:///C:/malware.exe", false)]
        [InlineData("//github.com/x", false)]
        [InlineData("", false)]
        public void IsAllowedUrl_SoloHttpsDeGitHub(string url, bool expected) =>
            Assert.Equal(expected, Make(new Gh(), "k").IsAllowedUrl(url));

        [Fact]
        public async Task ApiBaseNoHttps_SeRechaza()
        {
            var (gh, pub, _) = Publish("1.2.0");
            var info = await Make(gh, pub, "http://api.github.com").CheckAsync();
            Assert.Equal(UpdateStatus.Failed, info.Status);
        }

        [Fact]
        public async Task RepoConCaracteresRaros_SeRechaza()
        {
            var (gh, pub, _) = Publish("1.2.0");
            var u = new AppUpdater(new Version(1, 0, 0), "../../etc/passwd", pub, gh);
            Assert.Equal(UpdateStatus.Failed, (await u.CheckAsync()).Status);
        }

        [Fact]
        public async Task AssetEnHostAjeno_SeRechaza_AunqueLaFirmaSeaValida()
        {
            var (gh, pub, _) = Publish("1.2.0");
            gh.ExeUrl = "https://evil.example.com/SotheOptimizer.exe";
            var info = await Make(gh, pub).CheckAsync();
            Assert.Equal(UpdateStatus.Failed, info.Status);
            Assert.Contains("URL no permitida", info.Message);
        }

        [Fact]
        public async Task FirmaConBase64Roto_NoLanzaExcepcion()
        {
            var (gh, pub, _) = Publish("1.2.0", sigOverride: Encoding.ASCII.GetBytes("esto no es base64!!"));
            var info = await Make(gh, pub).CheckAsync();   // antes: FormatException sin capturar
            Assert.Equal(UpdateStatus.Failed, info.Status);
        }

        [Fact]
        public async Task TamanoDeclaradoEnormeEnElManifiesto_SeRechaza()
        {
            var (gh, pub, _) = Publish("1.2.0", size: AppUpdater.MaxExeBytes + 1);
            Assert.Equal(UpdateStatus.Failed, (await Make(gh, pub).CheckAsync()).Status);
        }

        [Fact]
        public async Task DescargaMasGrandeQueLoFirmado_SeDescarta_YNoQuedaNada()
        {
            var (gh, pub, exe) = Publish("1.2.0");
            gh.ExeBody = exe.Concat(new byte[1000]).ToArray();   // el servidor sirve mas bytes de los firmados
            var updater = Make(gh, pub);
            var stage = NewDir();
            var staged = await updater.DownloadAndStageAsync(await updater.CheckAsync(), stage);
            Assert.Equal(UpdateStatus.Failed, staged.Status);
            Assert.Empty(Directory.GetFiles(stage));   // ni el .part ni el exe
        }

        [Fact]
        public async Task ContentLengthMayorQueLoFirmado_SeCortaAntesDeDescargar()
        {
            var (gh, pub, _) = Publish("1.2.0");
            gh.ExeDeclaredLength = 10_000_000;
            var updater = Make(gh, pub);
            var staged = await updater.DownloadAndStageAsync(await updater.CheckAsync(), NewDir());
            Assert.Equal(UpdateStatus.Failed, staged.Status);
        }

        [Fact]
        public async Task DescargaCorrecta_SeGuardaPorStreaming_ConElHashFirmado()
        {
            var (gh, pub, exe) = Publish("1.2.0");
            var updater = Make(gh, pub);
            var stage = NewDir();
            var staged = await updater.DownloadAndStageAsync(await updater.CheckAsync(), stage);
            Assert.Equal(UpdateStatus.Staged, staged.Status);
            Assert.Equal(exe, File.ReadAllBytes(Path.Combine(stage, AppUpdater.PendingExe)));
            Assert.False(File.Exists(Path.Combine(stage, AppUpdater.PendingExe + ".part")));
        }

        [Fact]
        public void ScriptDeReemplazo_TieneTiempoMaximo_CopiaDeSeguridadYMarchaAtras()
        {
            var s = AppUpdater.BuildSwapScript(4321, @"C:\stage\SotheOptimizer.pending.exe", @"C:\app\SotheOptimizer.exe", @"C:\stage\pending.json");
            Assert.Contains("geq 45 goto leave", s);                                                       // no espera para siempre
            Assert.Contains(@"copy /y ""C:\app\SotheOptimizer.exe"" ""C:\app\SotheOptimizer.exe.old""", s);  // copia antes de mover
            Assert.Contains("if errorlevel 1 goto failed", s);                                             // si el move falla...
            Assert.Contains(@"move /y ""C:\app\SotheOptimizer.exe.old"" ""C:\app\SotheOptimizer.exe""", s);  // ...se restaura
            Assert.Contains(@"find /i ""SotheOptimizer.exe""", s);                                         // espera por nombre de imagen
            Assert.True(s.IndexOf(":failed", StringComparison.Ordinal) < s.IndexOf("start \"\"", StringComparison.Ordinal));
        }

        [Fact]
        public void ScriptDeReemplazo_DuplicaElPorcientoDeLasRutas()
        {
            var s = AppUpdater.BuildSwapScript(7, @"C:\st%TEMP%\a.exe", @"C:\ap%PATH%\b.exe", @"C:\st\p.json");
            Assert.Contains("%%TEMP%%", s);
            Assert.DoesNotContain("st%TEMP%", s.Replace("%%TEMP%%", ""));
        }

        [Fact]
        public void TryApplyStaged_ConPreparadoCorrupto_NoEscribeScript()
        {
            var stage = NewDir();
            File.WriteAllBytes(Path.Combine(stage, AppUpdater.PendingExe), Encoding.ASCII.GetBytes("otro"));
            File.WriteAllText(Path.Combine(stage, AppUpdater.PendingInfo), "{\"version\":\"9.0.0\",\"sha256\":\"AA\"}");
            Assert.False(AppUpdater.TryApplyStaged(@"C:\app\SotheOptimizer.exe", stage, new Version(1, 0, 0), out _));
            Assert.False(File.Exists(Path.Combine(stage, "apply-update.cmd")));
        }
    }
}
