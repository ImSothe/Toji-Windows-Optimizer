using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Sothe.Tests
{
    public class TimerResolutionTests
    {
        [Fact]
        public void ScriptDeInstalacion_RegistraTareaAlLogonConElExe()
        {
            var s = TimerResolution.BuildInstallScript(@"C:\Program Files\Sothe\SotheOptimizer.exe");
            Assert.Contains("New-ScheduledTaskAction -Execute 'C:\\Program Files\\Sothe\\SotheOptimizer.exe' -Argument '--timer-resident --resolution 5000'", s);
            Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", s);
            Assert.Contains("-RunLevel Highest", s);
            Assert.Contains($"-TaskName '{TimerResolution.TaskName}'", s);
            Assert.Contains("Start-ScheduledTask", s);
        }

        [Fact]
        public void ScriptDeInstalacion_UsaLaResolucionCalibrada()
        {
            var s = TimerResolution.BuildInstallScript(@"C:\x\app.exe", 5150);
            Assert.Contains("-Argument '--timer-resident --resolution 5150'", s);
        }

        // La clave GlobalTimerResolutionRequests es una operacion "reg" del ajuste (con valor previo y orden de reversion),
        // no algo que el script escriba o ponga a 0 por su cuenta
        [Fact]
        public void LosScripts_NoTocanLaClaveGlobalDelRegistro()
        {
            Assert.DoesNotContain("GlobalTimerResolutionRequests", TimerResolution.BuildInstallScript(@"C:\x\app.exe"));
            Assert.DoesNotContain("GlobalTimerResolutionRequests", TimerResolution.BuildUninstallScript());
        }

        [Theory]
        [InlineData("--timer-resident --resolution 5150", 5150u)]
        [InlineData("--timer-resident", 5000u)]
        [InlineData("--timer-resident --resolution abc", 5000u)]
        [InlineData("--timer-resident --resolution 10", 5000u)]          // mas fino de 0.5 ms: se ignora
        [InlineData("--timer-resident --resolution 999999999", 5000u)]   // mas grueso que el defecto de Windows
        [InlineData("--timer-resident --resolution", 5000u)]
        public void ParseResolution_AceptaSoloValoresRazonables(string line, uint expected) =>
            Assert.Equal(expected, TimerResolution.ParseResolution(line.Split(' ')));

        [Fact]
        public void Score_PremiaAcercarseAUnMilisegundo_YCastigaElJitter()
        {
            Assert.True(TimerResolution.Score(1.16, 0.22) < TimerResolution.Score(1.54, 0.16));   // cifras medidas en el PC de desarrollo
            Assert.True(TimerResolution.Score(1.20, 0.10) < TimerResolution.Score(1.20, 0.50));
        }

        [Fact]
        public void ChooseBest_EligeLaMejor_YEnEmpateLaMasFina()
        {
            var best = TimerResolution.ChooseBest(new[]
            {
                new TimerResolution.Probe(5000, 1.54, 0.16), new TimerResolution.Probe(5150, 1.16, 0.22), new TimerResolution.Probe(5300, 1.19, 0.23)
            });
            Assert.Equal(5150u, best.Resolution);
            var tie = TimerResolution.ChooseBest(new[] { new TimerResolution.Probe(5200, 1.2, 0.2), new TimerResolution.Probe(5100, 1.2, 0.2) });
            Assert.Equal(5100u, tie.Resolution);
            Assert.Throws<ArgumentException>(() => TimerResolution.ChooseBest(Array.Empty<TimerResolution.Probe>()));
        }

        [Fact]
        public void Calibrate_DevuelveUnCandidatoMedido_YNoDejaNadaPedido()
        {
            if (!OperatingSystem.IsWindows()) return;
            var best = TimerResolution.Calibrate(samples: 5, steps: 3);
            Assert.True(best.Resolution >= 5000 && best.Resolution <= 156_250);
            Assert.False(TimerResolution.IsApplied);
        }

        [Fact]
        public void ScriptDeInstalacion_EscapaLasComillasDelExe()
        {
            var s = TimerResolution.BuildInstallScript(@"C:\o'brien\app.exe");
            Assert.Contains("'C:\\o''brien\\app.exe'", s);   // comilla simple duplicada = escape de PowerShell
        }

        [Fact]
        public void ScriptDeDesinstalacion_QuitaLaTareaYMataElResidente()
        {
            var s = TimerResolution.BuildUninstallScript();
            Assert.Contains($"Unregister-ScheduledTask -TaskName '{TimerResolution.TaskName}'", s);
            Assert.Contains("--timer-resident", s);
            Assert.Contains("Stop-Process", s);
        }

        [Fact]
        public void ScriptDeDesinstalacion_NoSeMataASiMismo()
        {
            // PowerShell recibe el script por -Command, asi que su propia linea de comandos contiene "--timer-resident":
            // el filtro por $PID tiene que ir antes de Stop-Process
            var s = TimerResolution.BuildUninstallScript();
            int exclude = s.IndexOf("Where-Object { $_.ProcessId -ne $PID }", StringComparison.Ordinal);
            Assert.True(exclude > 0, "falta excluir el propio proceso de PowerShell");
            Assert.True(exclude < s.IndexOf("Stop-Process", StringComparison.Ordinal));
        }

        [Fact]
        public void CurrentMs_EsCeroFueraDeWindows()
        {
            if (!OperatingSystem.IsWindows()) Assert.Equal(0, TimerResolution.CurrentMs());
        }

        // El tweak real debe seguir usando los sentinelas que el motor enruta a C# (no PowerShell)
        [Fact]
        public void TweakTimerFijo_UsaLosSentinelas()
        {
            using var d = new TestDirs();
            var t = d.Engine().LoadTweaks().Single(x => x.Id == "timer_res_fixed");
            Assert.Equal(2, t.Ops.Count);
            var reg = t.Ops[0];
            Assert.Equal("reg", reg.Type);
            Assert.Equal("GlobalTimerResolutionRequests", reg.Name);
            var op = t.Ops[1];
            Assert.Equal("command", op.Type);
            Assert.Equal(TimerResolution.InstallSentinel, op.Apply);
            Assert.Equal(TimerResolution.UninstallSentinel, op.Revert);
        }
    }
}
