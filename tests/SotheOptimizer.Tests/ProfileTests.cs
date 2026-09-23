using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Sothe.Tests
{
    public class ProfileTests
    {
        private static string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

        [Fact]
        public void GuardarPerfil_VaAlFicheroDeUsuario_SinTocarLosOficiales()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var official = Path.Combine(d.Data, "profiles.json");
            var before = Sha(official);

            eng.SaveProfile("Mi setup", new List<string> { "game_mode" });

            Assert.Equal(before, Sha(official));
            Assert.True(File.Exists(eng.UserProfilesPath));
            var all = eng.LoadProfiles();
            var mine = Assert.Single(all, p => p.Id == "mi_setup");
            Assert.True(mine.IsCustom);
            Assert.Contains(all, p => p.Id == "competitivo" && !p.IsCustom);
            Assert.StartsWith("{\n  \"profiles\"", File.ReadAllText(eng.UserProfilesPath).Replace("\r\n", "\n"));   // camelCase
        }

        [Theory]
        [InlineData("Competitivo", "Competitivo_Custom")]
        [InlineData("  competitivo ", "competitivo_Custom")]
        [InlineData("CIBERSEGURIDAD", "CIBERSEGURIDAD_Custom")]
        [InlineData("ciber", "ciber_Custom")]
        [InlineData("Límpio", "Límpio_Custom")]
        public void NombresOficiales_EstanReservados(string name, string expectedSuggestion)
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            Assert.True(eng.IsReservedProfileName(name, out var suggestion));
            Assert.Equal(expectedSuggestion, suggestion);
            var ex = Assert.Throws<InvalidOperationException>(() => eng.SaveProfile(name, new List<string> { "game_mode" }));
            Assert.Contains("perfil oficial", ex.Message);
            Assert.False(File.Exists(eng.UserProfilesPath));
        }

        [Theory]
        [InlineData("Competitivo_Custom")]
        [InlineData("Competitivo 2")]
        [InlineData("Mi Ciber")]
        public void NombresParecidos_SePermiten(string name)
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            Assert.False(eng.IsReservedProfileName(name, out _));
            eng.SaveProfile(name, new List<string> { "game_mode" });
            Assert.Contains(eng.LoadProfiles(), p => p.Name == name && p.IsCustom);
        }

        [Fact]
        public void PerfilesPropiosAntiguos_SeMigranAlFicheroDeUsuario()
        {
            using var d = new TestDirs();
            var official = Path.Combine(d.Data, "profiles.json");
            // Asi los guardaba la version anterior: dentro de data\profiles.json
            var doc = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(official), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            doc.Profiles.Add(new Profile { Id = "sothe_pro", Name = "Sothe Pro", Desc = SotheEngine.CustomProfileDesc, Tweaks = new List<string> { "game_mode" } });
            File.WriteAllText(official, JsonSerializer.Serialize(doc));

            var eng = d.Engine();   // la migracion se hace al crear el motor

            Assert.DoesNotContain("sothe_pro", File.ReadAllText(official));
            Assert.Contains("sothe_pro", File.ReadAllText(eng.UserProfilesPath));
            Assert.Contains(eng.LoadProfiles(), p => p.Id == "sothe_pro" && p.IsCustom);
        }

        [Fact]
        public void ExportarImportar_IdaYVuelta()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var ids = new[] { "game_mode", "mouse_accel_off" };
            var code = eng.ExportCode(ids);
            Assert.StartsWith(SotheEngine.ExportPrefix, code);
            Assert.Equal(ids, eng.ImportCode(code));
            Assert.Equal(ids, eng.ImportCode(code.Substring(SotheEngine.ExportPrefix.Length)));
        }
    }
}
