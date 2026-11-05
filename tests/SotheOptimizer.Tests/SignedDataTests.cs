using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sothe.Tests
{
    // data\ solo manda si lo firmo el autor. Cualquier otra cosa (a mano, retocado, otra clave, firma rota...) se
    // ignora y la app usa los JSON incrustados en el .exe
    public class SignedDataTests
    {
        private const string SoloUno = "{ \"tweaks\": [ { \"id\": \"solo_disco\", \"name\": \"Solo disco\" } ] }";

        private static string Tweaks(TestDirs d) => Path.Combine(d.Data, "tweaks.json");
        // La copia de disco (un solo ajuste) no se ha usado: mandan los ajustes incrustados en el .exe
        private static void AssertUsaLoIncrustado(SotheEngine eng)
        {
            var tweaks = eng.LoadTweaks();
            Assert.DoesNotContain(tweaks, t => t.Id == "solo_disco");
            Assert.Contains(tweaks, t => t.Id == "game_mode");
        }

        [Fact]
        public void Firmado_YSinTocar_SeUsa()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            d.Sign();
            Assert.Equal("solo_disco", Assert.Single(d.Engine().LoadTweaks()).Id);
        }

        [Fact]
        public void PuestoAMano_SinFirma_SeIgnora_YQuedaEnElLog()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            var eng = d.Engine();

            AssertUsaLoIncrustado(eng);
            Assert.Contains("tweaks.json ignored (no signed manifest)", File.ReadAllText(Path.Combine(d.Root, "sothe.log")));
        }

        [Fact]
        public void ManipuladoDespuesDeFirmar_SeIgnora()
        {
            using var d = new TestDirs();
            d.Sign();
            // Un solo cambio tras la firma: un ajuste "safe" pasa a "extreme"
            var text = File.ReadAllText(Tweaks(d));
            var i = text.IndexOf("\"danger\": \"safe\"", StringComparison.Ordinal);
            File.WriteAllText(Tweaks(d), text.Substring(0, i) + "\"danger\": \"extreme\"" + text.Substring(i + "\"danger\": \"safe\"".Length));

            var tweaks = d.Engine().LoadTweaks();
            Assert.Equal("safe", tweaks.Single(t => t.Id == "game_mode").Danger);
            Assert.Contains("tampered", File.ReadAllText(Path.Combine(d.Root, "sothe.log")));
        }

        [Fact]
        public void ManifiestoRetocadoConElNuevoHash_SeIgnora()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            d.Sign();
            // El atacante cambia el fichero y actualiza su SHA-256 en manifest.json, pero no puede rehacer la firma
            var evil = "{ \"tweaks\": [ { \"id\": \"malo\", \"name\": \"Malo\" } ] }";
            File.WriteAllText(Tweaks(d), evil);
            var manifestPath = Path.Combine(d.Data, "manifest.json");
            var manifest = JsonSerializer.Deserialize<OtaManifest>(File.ReadAllBytes(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            manifest.Files["tweaks.json"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Tweaks(d))));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { version = manifest.Version, files = manifest.Files }));

            Assert.DoesNotContain(d.Engine().LoadTweaks(), t => t.Id == "malo");
        }

        [Fact]
        public void FirmadoConOtraClave_SeIgnora()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ManifestSigner.SignDirectory(d.Data, attacker, 7);

            AssertUsaLoIncrustado(d.Engine());
        }

        [Theory]
        [InlineData("esto no es base64 !!")]
        [InlineData("")]
        [InlineData("AAAA")]
        public void FirmaCorrupta_SeIgnora_SinRomper(string signature)
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            d.Sign();
            File.WriteAllText(Path.Combine(d.Data, "manifest.sig"), signature);

            AssertUsaLoIncrustado(d.Engine());
        }

        [Fact]
        public void ManifiestoBorrado_SeIgnora()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            d.Sign();
            File.Delete(Path.Combine(d.Data, "manifest.json"));

            AssertUsaLoIncrustado(d.Engine());
        }

        [Fact]
        public void FicheroQueNoFiguraEnElManifiesto_SeIgnora()
        {
            using var d = new TestDirs();
            File.Delete(Path.Combine(d.Data, "apps.json"));
            d.Sign();   // firma sin apps.json
            File.WriteAllText(Path.Combine(d.Data, "apps.json"), "{ \"apps\": [ { \"id\": \"Malware.Falso\", \"name\": \"Discord\" } ] }");

            Assert.DoesNotContain(d.Engine().LoadApps(), a => a.Id == "Malware.Falso");
        }

        [Fact]
        public void SinClavePublica_NuncaSeConfiaEnDisco()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            d.Sign();
            AssertUsaLoIncrustado(new SotheEngine(d.BaseDir, d.Root, ""));
            AssertUsaLoIncrustado(new SotheEngine(d.BaseDir, d.Root, "clave-rota"));
        }

        [Fact]
        public void FirmadoPeroNoMasNuevoQueLoIncrustado_UsaLoIncrustado()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno);
            // Manifiesto bien firmado pero con la misma version que los datos del .exe (0 en los tests)
            var files = new System.Collections.Generic.Dictionary<string, string> { ["tweaks.json"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Tweaks(d)))) };
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new { version = SotheEngine.EmbeddedDataVersion, files });
            File.WriteAllBytes(Path.Combine(d.Data, "manifest.json"), manifest);
            File.WriteAllText(Path.Combine(d.Data, "manifest.sig"), Convert.ToBase64String(d.Key.SignData(manifest, HashAlgorithmName.SHA256)));

            AssertUsaLoIncrustado(d.Engine());
        }

        [Fact]
        public void FirmadoConBom_SeLeeBien()
        {
            using var d = new TestDirs();
            File.WriteAllText(Tweaks(d), SoloUno, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            d.Sign();
            Assert.Equal("solo_disco", Assert.Single(d.Engine().LoadTweaks()).Id);
        }

        [Fact]
        public void ElFirmadorRechazaVersionesNoPositivas()
        {
            using var d = new TestDirs();
            Assert.Throws<ArgumentOutOfRangeException>(() => d.Sign(0));
        }
    }
}
