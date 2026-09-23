using System.IO;
using System.Linq;
using Xunit;

namespace Toji.Tests
{
    // Distribucion en un unico .exe: sin carpeta data\ el motor usa los JSON incrustados; si la hay y esta firmada (OTA), manda el disco
    public class EmbeddedDataTests
    {
        private static TestDirs WithoutDataFolder()
        {
            var d = new TestDirs();
            Directory.Delete(d.Data, true);
            return d;
        }

        [Fact]
        public void SinCarpetaData_CargaLosJsonIncrustados()
        {
            using var disk = new TestDirs();
            using var d = WithoutDataFolder();
            var eng = d.Engine();

            Assert.Equal(disk.Engine().LoadTweaks().Count, eng.LoadTweaks().Count);
            Assert.NotEmpty(eng.LoadApps());
            Assert.NotEmpty(eng.LoadFeatures());
            Assert.Contains(eng.LoadProfiles(), p => p.Id == "competitivo" && !p.IsCustom);
            Assert.True(eng.IsReservedProfileName("Competitivo", out _));
        }

        [Fact]
        public void SinCarpetaData_GuardarPerfilFunciona()
        {
            using var d = WithoutDataFolder();
            var eng = d.Engine();

            eng.SaveProfile("Mi setup", new() { "game_mode" });

            var all = eng.LoadProfiles();
            Assert.Contains(all, p => p.Id == "mi_setup" && p.IsCustom);
            Assert.Contains(all, p => p.Id == "competitivo" && !p.IsCustom);
            Assert.False(Directory.Exists(d.Data));   // los oficiales siguen saliendo del .exe
        }

        [Fact]
        public void DataFirmadaEnDisco_TienePrioridadSobreLoIncrustado()
        {
            using var d = new TestDirs();
            File.WriteAllText(Path.Combine(d.Data, "tweaks.json"), "{ \"tweaks\": [ { \"id\": \"solo_disco\", \"name\": \"Solo disco\" } ] }");
            d.Sign();

            var t = Assert.Single(d.Engine().LoadTweaks());
            Assert.Equal("solo_disco", t.Id);
        }

        [Fact]
        public void ReadDataText_FicheroDesconocido_DevuelveNull()
        {
            using var d = WithoutDataFolder();
            Assert.Null(d.Engine().ReadDataText("no-existe.json"));
        }
    }
}
