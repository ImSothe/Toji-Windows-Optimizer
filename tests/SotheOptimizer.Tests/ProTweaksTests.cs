using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Sothe.Tests
{
    // Paywall inteligente: los ajustes gratuitos se aplican sin licencia; basta uno PRO para pedirla
    public class ProTweaksTests
    {
        [Fact]
        public void TodosLosTweaks_DeclaranIsProExplicitamente()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "data", "tweaks.json")));
            var sinMarcar = doc.RootElement.GetProperty("tweaks").EnumerateArray()
                .Where(t => !t.TryGetProperty("isPro", out var v) || (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False))
                .Select(t => t.GetProperty("id").GetString()).ToList();
            Assert.Empty(sinMarcar);
        }

        [Theory]
        [InlineData("telemetry_off")]
        [InlineData("disable_telemetry_tasks")]
        [InlineData("advertising_id_off")]
        [InlineData("activity_history_off")]
        [InlineData("bg_apps_off")]
        [InlineData("nvidia_telemetry_off")]
        public void AjustesBasicos_SonGratis(string id)
        {
            using var d = new TestDirs();
            Assert.False(d.Engine().LoadTweaks().Single(t => t.Id == id).IsPro);
        }

        [Theory]
        [InlineData("timer_resolution")]
        [InlineData("timer_res_fixed")]
        [InlineData("game_mode")]
        [InlineData("fullscreen_optim_off")]
        [InlineData("sysmain_off")]
        [InlineData("network_latency")]
        [InlineData("nagle_off")]
        [InlineData("win32_priority")]
        public void AjustesDeRendimiento_SonPro(string id)
        {
            using var d = new TestDirs();
            Assert.True(d.Engine().LoadTweaks().Single(t => t.Id == id).IsPro);
        }

        [Fact]
        public void SoloGratuitos_NoPidenLicencia()
        {
            using var d = new TestDirs();
            var tweaks = d.Engine().LoadTweaks();
            Assert.Empty(SotheEngine.ProTweaksToApply(tweaks, new[] { "telemetry_off", "bg_apps_off", "dark_mode" }));
        }

        [Fact]
        public void UnSoloPro_BastaParaPedirLicencia_YSeListaSoloEse()
        {
            using var d = new TestDirs();
            var tweaks = d.Engine().LoadTweaks();
            var pro = SotheEngine.ProTweaksToApply(tweaks, new[] { "telemetry_off", "timer_res_fixed", "bg_apps_off" });
            Assert.Equal("timer_res_fixed", Assert.Single(pro).Id);
        }

        [Fact]
        public void IdsDesconocidosOVacios_NoRompen()
        {
            using var d = new TestDirs();
            var tweaks = d.Engine().LoadTweaks();
            Assert.Empty(SotheEngine.ProTweaksToApply(tweaks, new[] { "no_existe" }));
            Assert.Empty(SotheEngine.ProTweaksToApply(tweaks, null));
            Assert.Empty(SotheEngine.ProTweaksToApply(null, new[] { "game_mode" }));
        }

        [Fact]
        public void EditarDataEnDisco_NoConvierteEnGratisUnAjustePro()
        {
            using var d = new TestDirs();
            // Alguien edita data\tweaks.json junto al .exe para marcar como gratis un ajuste PRO: sin firma se ignora
            File.WriteAllText(Path.Combine(d.Data, "tweaks.json"),
                "{ \"tweaks\": [ { \"id\": \"game_mode\", \"name\": \"Activar Game Mode\", \"isPro\": false } ] }");
            var tweaks = d.Engine().LoadTweaks();
            Assert.True(tweaks.Count > 1);   // manda la copia incrustada
            var t = tweaks.Single(x => x.Id == "game_mode");
            Assert.True(t.IsPro);
            Assert.Single(SotheEngine.ProTweaksToApply(tweaks, new[] { "game_mode" }));
        }

        [Fact]
        public void DataFirmadaPorElAutor_PuedeLiberarUnAjustePro()
        {
            using var d = new TestDirs();
            File.WriteAllText(Path.Combine(d.Data, "tweaks.json"),
                "{ \"tweaks\": [ { \"id\": \"game_mode\", \"name\": \"Activar Game Mode\", \"isPro\": false } ] }");
            d.Sign();
            Assert.False(Assert.Single(d.Engine().LoadTweaks()).IsPro);
        }

        [Fact]
        public void DataFirmada_PuedeMarcarComoProUnAjusteGratis()
        {
            using var d = new TestDirs();
            File.WriteAllText(Path.Combine(d.Data, "tweaks.json"),
                "{ \"tweaks\": [ { \"id\": \"telemetry_off\", \"name\": \"Desactivar telemetria\", \"isPro\": true } ] }");
            d.Sign();
            Assert.True(Assert.Single(d.Engine().LoadTweaks()).IsPro);
        }

        [Fact]
        public void SinPropiedadIsPro_ElAjusteEsGratis()
        {
            using var d = new TestDirs();
            File.WriteAllText(Path.Combine(d.Data, "tweaks.json"), "{ \"tweaks\": [ { \"id\": \"nuevo\", \"name\": \"Nuevo\" } ] }");
            d.Sign();
            Assert.False(Assert.Single(d.Engine().LoadTweaks()).IsPro);
        }
    }
}
