using System;
using System.Linq;
using Xunit;

namespace Toji.Tests
{
    public class WindowsToolsTests
    {
        [Fact]
        public void TodasLasAppsYCaracteristicasReales_SonValidas()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var apps = eng.LoadApps();
            var feats = eng.LoadFeatures();
            Assert.Equal(31, apps.Count);
            Assert.Equal(6, feats.Count);
            Assert.All(apps, a => Assert.True(Winget.IsValidId(a.Id), a.Id));
            Assert.All(feats, f => Assert.True(FeatureCommands.IsValidName(f.Feature), f.Feature));
        }

        [Theory]
        [InlineData("Valve.Steam; Remove-Item C:\\ -Recurse")]
        [InlineData("Valve.Steam && calc")]
        [InlineData("$(evil)")]
        [InlineData("")]
        public void IdsConInyeccion_SeRechazan(string id)
        {
            Assert.False(Winget.IsValidId(id));
            Assert.Throws<ArgumentException>(() => Winget.InstallScript(new[] { "Valve.Steam", id }));
        }

        [Fact]
        public void ScriptDeInstalacion_ComoLaVersionPowerShell() =>
            Assert.Equal("winget install --id Valve.Steam -e --accept-source-agreements --accept-package-agreements; " +
                         "winget install --id Notepad++.Notepad++ -e --accept-source-agreements --accept-package-agreements",
                         Winget.InstallScript(new[] { "Valve.Steam", "Notepad++.Notepad++" }));

        [Fact]
        public void CaracteristicaConInyeccion_SeRechaza() =>
            Assert.Throws<ArgumentException>(() => FeatureCommands.SetScript("NetFx3'; Remove-Item C:\\ #", true));

        [Fact]
        public void ScriptDeCaracteristica_DevuelveSiPideReiniciar()
        {
            Assert.Equal("(Enable-WindowsOptionalFeature -Online -FeatureName 'NetFx3' -All -NoRestart -ErrorAction Stop).RestartNeeded", FeatureCommands.SetScript("NetFx3", true));
            Assert.StartsWith("(Disable-WindowsOptionalFeature -Online -FeatureName 'NetFx3'", FeatureCommands.SetScript("NetFx3", false));
        }

        [Fact]
        public void EstadosDeWmi() =>
            Assert.Equal(new[] { FeatureState.Enabled, FeatureState.Disabled, FeatureState.Unavailable, FeatureState.Unknown },
                         new uint[] { 1, 2, 3, 4 }.Select(FeatureCommands.FromInstallState));

        [Fact]
        public void ExportDeWinget_SeLeeSinDistinguirMayusculas()
        {
            var ids = Winget.ParseExport("{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"Valve.Steam\"},{\"PackageIdentifier\":\"Git.Git\"}]},{\"Packages\":[{\"PackageIdentifier\":\"9NBLGGH4NNS1\"}]}]}");
            Assert.Equal(3, ids.Count);
            Assert.Contains("valve.steam", ids);
        }

        [Fact]
        public void ExportVacioORaro_NoRompe()
        {
            Assert.Empty(Winget.ParseExport(""));
            Assert.Empty(Winget.ParseExport("{}"));
            Assert.Empty(Winget.ParseExport("{\"Sources\":{}}"));
        }

        [Fact]
        public void Gpu_PrefiereDedicada_EIgnoraVirtuales()
        {
            Assert.Equal("NVIDIA GeForce RTX 4070", GpuDrivers.PickGpu(new[] { "Microsoft Basic Display Adapter", "Intel(R) UHD Graphics 770", "NVIDIA GeForce RTX 4070" }));
            Assert.Equal("Intel(R) UHD Graphics 770", GpuDrivers.PickGpu(new[] { "Parsec Virtual Display Adapter", "Intel(R) UHD Graphics 770" }));
            Assert.Null(GpuDrivers.PickGpu(new[] { "Microsoft Remote Display Adapter" }));
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 4070", "NVIDIA")]
        [InlineData("AMD Radeon RX 7800 XT", "AMD")]
        [InlineData("Intel(R) Arc(TM) A770", "Intel")]
        [InlineData("Matrox G200", null)]
        public void Gpu_WebOficialDelFabricante(string gpu, string vendor) => Assert.Equal(vendor, GpuDrivers.DriverPage(gpu).vendor);
    }
}

namespace Toji.Tests
{
    public class FeatureFallbackTests
    {
        [Fact]
        public void EstadosDePowerShell_SeInterpretan()
        {
            var st = FeatureCommands.ParseStates("NetFx3=Enabled\r\nTelnetClient=Disabled\r\nMicrosoft-Hyper-V-All=DisabledWithPayloadRemoved\r\nbasura\r\n",
                new[] { "NetFx3", "TelnetClient", "Microsoft-Hyper-V-All", "Containers-DisposableClientVM" });
            Assert.Equal(FeatureState.Enabled, st["NetFx3"]);
            Assert.Equal(FeatureState.Disabled, st["TelnetClient"]);
            Assert.Equal(FeatureState.Disabled, st["Microsoft-Hyper-V-All"]);
            Assert.Equal(FeatureState.Unavailable, st["Containers-DisposableClientVM"]);   // no existe en esta edicion
        }

        [Fact]
        public void ScriptDeConsulta_SoloConNombresValidos()
        {
            Assert.Contains("'NetFx3','TelnetClient'", FeatureCommands.QueryScript(new[] { "NetFx3", "TelnetClient" }));
            Assert.Throws<System.ArgumentException>(() => FeatureCommands.QueryScript(new[] { "x'); calc; ('" }));
        }

        [Fact]
        public void ScriptDeConsulta_FuncionaEnPowerShellReal()
        {
            // Sintaxis valida en PowerShell 7 (en Linux Get-WindowsOptionalFeature no existe: el error se detecta)
            PowerShellRunner.Executable = "pwsh";
            var ex = Record.Exception(() => PowerShellRunner.RunCapture(FeatureCommands.QueryScript(new[] { "NetFx3" }), 60000));
            if (ex != null) Assert.DoesNotContain("ParserError", ex.Message);
        }
    }
}
