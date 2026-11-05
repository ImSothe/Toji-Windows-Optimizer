using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Sothe.Tests
{
    // El idioma es estado global (Loc.Language): estas pruebas no corren en paralelo y siempre lo dejan en ingles
    [Collection("loc")]
    public class LocalizationTests : IDisposable
    {
        public LocalizationTests() => Loc.SetLanguage(Loc.English);
        public void Dispose() => Loc.SetLanguage(Loc.English);

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "SotheOptimizer.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("No se encuentra la raiz del repositorio");
        }

        private static Dictionary<string, string> En => Strings.En;
        private static Dictionary<string, string> Es => Strings.Es;

        [Fact]
        public void EnglishIsTheDefault() => Assert.Equal(Loc.English, Loc.Language);

        [Fact]
        public void EveryKeyExistsInBothLanguages()
        {
            Assert.Empty(En.Keys.Except(Es.Keys));
            Assert.Empty(Es.Keys.Except(En.Keys));
        }

        [Fact]
        public void PlaceholdersMatchBetweenLanguages()
        {
            foreach (var k in En.Keys)
            {
                var a = Regex.Matches(En[k], @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
                var b = Regex.Matches(Es[k], @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
                Assert.True(a.SequenceEqual(b), $"{k}: los marcadores no coinciden entre idiomas");
            }
        }

        [Fact]
        public void EnglishTextHasNoSpanishAccentsOrOpeningMarks()
        {
            var bad = En.Where(kv => Regex.IsMatch(kv.Value, "[áéíóúñ¿¡«»]") && kv.Key != "lang.name.es").Select(kv => kv.Key).ToList();
            Assert.Empty(bad);
        }

        [Fact]
        public void NoTextIsEmpty()
        {
            Assert.DoesNotContain(En, kv => string.IsNullOrWhiteSpace(kv.Value));
            Assert.DoesNotContain(Es, kv => string.IsNullOrWhiteSpace(kv.Value));
        }

        // Una clave que se pide desde el codigo o el XAML y no existe se veria como "clave.rara" en pantalla
        [Fact]
        public void EveryKeyUsedInCodeAndXamlExists()
        {
            var src = Path.Combine(RepoRoot(), "src", "SotheOptimizer");
            var used = new HashSet<string>();
            foreach (var f in Directory.GetFiles(src, "*.cs").Where(f => !f.EndsWith("Strings.cs")))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Loc\.T\(\s*""([^""]+)""\s*[,)]"))
                    used.Add(m.Groups[1].Value);
            foreach (var f in Directory.GetFiles(src, "*.xaml"))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{Binding \[([^\]]+)\], Source=\{x:Static local:Loc\.Instance\}"))
                    used.Add(m.Groups[1].Value);
            Assert.NotEmpty(used);
            Assert.Empty(used.Where(k => !En.ContainsKey(k)));
        }

        [Fact]
        public void SwitchingLanguageChangesTextAndFallsBackToEnglish()
        {
            Assert.Equal("Cancel", Loc.T("dlg.cancel"));
            Loc.SetLanguage("es");
            Assert.Equal("Cancelar", Loc.T("dlg.cancel"));
            Assert.Equal("Cancelar", Loc.Instance["dlg.cancel"]);
            Assert.Equal("clave.inexistente", Loc.T("clave.inexistente"));
            Loc.SetLanguage("fr");   // idioma desconocido: ingles
            Assert.Equal(Loc.English, Loc.Language);
        }

        [Fact]
        public void FormatsArguments()
        {
            Assert.Equal("Reverting: Game Mode", Loc.T("prog.revert", "Game Mode"));
            Loc.SetLanguage("es");
            Assert.Equal("Revirtiendo: Game Mode", Loc.T("prog.revert", "Game Mode"));
        }

        [Fact]
        public void NotifiesWhenTheLanguageChanges()
        {
            int changed = 0, bindings = 0;
            Action h = () => changed++;
            Loc.LanguageChanged += h;
            Loc.Instance.PropertyChanged += (s, e) => { if (e.PropertyName == "Item[]") bindings++; };
            try
            {
                Loc.SetLanguage("es");
                Loc.SetLanguage("es");   // sin cambio: no avisa
                Loc.SetLanguage("en");
            }
            finally { Loc.LanguageChanged -= h; }
            Assert.Equal(2, changed);
            Assert.True(bindings >= 2);
        }

        [Fact]
        public void PickChoosesTheSpanishBlockOnlyInSpanishAndOnlyIfItExists()
        {
            Assert.Equal("Name", Loc.Pick("Name", "Nombre"));
            Loc.SetLanguage("es");
            Assert.Equal("Nombre", Loc.Pick("Name", "Nombre"));
            Assert.Equal("Name", Loc.Pick("Name", null));
            Assert.Equal("Name", Loc.Pick("Name", ""));
        }

        [Fact]
        public void TheChosenLanguageIsSavedAndLoadedFromTheDataFolder()
        {
            using var d = new TestDirs();
            Loc.SetLanguage("es");
            Loc.Save(d.Root);
            Loc.SetLanguage("en");
            Loc.Load(d.Root);
            Assert.Equal(Loc.Spanish, Loc.Language);
        }

        [Fact]
        public void ACorruptOrMissingSettingsFileLeavesTheLanguageAlone()
        {
            using var d = new TestDirs();
            Loc.Load(d.Root);   // no existe
            Assert.Equal(Loc.English, Loc.Language);
            File.WriteAllText(Loc.SettingsPath(d.Root), "{ no es json");
            Loc.Load(d.Root);
            Assert.Equal(Loc.English, Loc.Language);
        }

        // ---- Datos: cada elemento visible tiene su texto en espanol ----
        [Fact]
        public void EveryTweakHasSpanishText()
        {
            using var d = new TestDirs();
            foreach (var t in d.Engine().LoadTweaks())
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Name), t.Id);
                Assert.False(string.IsNullOrWhiteSpace(t.Es?.Name), t.Id + ": falta es.name");
                Assert.False(string.IsNullOrWhiteSpace(t.Es?.Desc), t.Id + ": falta es.desc");
                Assert.Equal(!string.IsNullOrEmpty(t.Warning), !string.IsNullOrEmpty(t.Es?.Warning));
            }
        }

        [Fact]
        public void EveryAppFeatureAndProfileHasSpanishText()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            Assert.All(eng.LoadApps(), a => Assert.False(string.IsNullOrWhiteSpace(a.Es?.Desc), a.Id));
            Assert.All(eng.LoadFeatures(), f => { Assert.False(string.IsNullOrWhiteSpace(f.Es?.Name), f.Feature); Assert.False(string.IsNullOrWhiteSpace(f.Es?.Desc), f.Feature); });
            Assert.All(eng.LoadProfiles().Where(p => !p.IsCustom), p => { Assert.False(string.IsNullOrWhiteSpace(p.Es?.Name), p.Id); Assert.False(string.IsNullOrWhiteSpace(p.Es?.Desc), p.Id); });
        }

        [Fact]
        public void EveryCategoryHasATranslation()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var cats = eng.LoadTweaks().Select(t => t.Category).Concat(eng.LoadApps().Select(a => a.Category)).Distinct();
            Assert.Empty(cats.Where(c => !En.ContainsKey("cat." + c)));
        }

        [Fact]
        public void DisplayTextFollowsTheLanguage()
        {
            using var d = new TestDirs();
            var t = d.Engine().LoadTweaks().Single(x => x.Id == "telemetry_off");
            Assert.Equal("Disable telemetry", t.DisplayName);
            Loc.SetLanguage("es");
            Assert.Equal("Desactivar telemetría", t.DisplayName);
            Assert.Contains("Windows Update", t.DisplayWarning);
        }

        [Fact]
        public void TheEnglishTextInTheDataFilesIsEnglish()
        {
            using var d = new TestDirs();
            var bad = d.Engine().LoadTweaks().Where(t => Regex.IsMatch(t.Name + t.Desc + t.Warning, "[áéíóúñ¿¡]")).Select(t => t.Id);
            Assert.Empty(bad);
        }

        // Los perfiles propios guardan "Perfil personalizado" como marca interna: se muestra en el idioma elegido
        [Fact]
        public void CustomProfileMarkerIsShownLocalized()
        {
            var p = new Profile { Id = "mine", Name = "Mine", Desc = SotheEngine.CustomProfileDesc };
            Assert.Equal("Custom profile", p.DisplayDesc);
            Loc.SetLanguage("es");
            Assert.Equal("Perfil personalizado", p.DisplayDesc);
        }

        [Theory]
        [InlineData("Competitive")]
        [InlineData("competitivo")]
        [InlineData("Cybersecurity")]
        [InlineData("Ciberseguridad")]
        [InlineData("Clean")]
        [InlineData("Limpio")]
        public void OfficialProfileNamesAreReservedInBothLanguages(string name)
        {
            using var d = new TestDirs();
            Assert.True(d.Engine().IsReservedProfileName(name, out _), name);
        }
    }
}
