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
            Assert.Contains("New-ScheduledTaskAction -Execute 'C:\\Program Files\\Sothe\\SotheOptimizer.exe' -Argument '--timer-resident'", s);
            Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", s);
            Assert.Contains("-RunLevel Highest", s);
            Assert.Contains($"-TaskName '{TimerResolution.TaskName}'", s);
            Assert.Contains("GlobalTimerResolutionRequests", s);
            Assert.Contains("Start-ScheduledTask", s);
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
            var op = Assert.Single(t.Ops);
            Assert.Equal("command", op.Type);
            Assert.Equal(TimerResolution.InstallSentinel, op.Apply);
            Assert.Equal(TimerResolution.UninstallSentinel, op.Revert);
        }
    }
}
