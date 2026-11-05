using System;
using System.Diagnostics;
using Xunit;

namespace Sothe.Tests
{
    // Bug 1: los comandos PowerShell que fallan ya no se dan por buenos.
    // Usa PowerShell 7 (pwsh) si esta, o powershell.exe 5.1 en Windows. Sin ninguno, los tests FALLAN (no pasan en falso).
    public class PowerShellRunnerTests
    {
        private static readonly string Exe = Detect();

        private static string Detect()
        {
            foreach (var exe in new[] { "pwsh", "powershell.exe" })
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(exe, "-NoProfile -Command exit 0") { UseShellExecute = false, CreateNoWindow = true });
                    p.WaitForExit();
                    if (p.ExitCode == 0) { PowerShellRunner.Executable = exe; return exe; }
                }
                catch { }
            }
            return null;
        }

        private static void Require() => Assert.True(Exe != null, "No hay PowerShell (pwsh ni powershell.exe) para ejecutar estos tests.");
        private static void RunOk(string cmd) { Require(); PowerShellRunner.Run(cmd); }
        private static Exception RunFail(string cmd, int timeoutMs = PowerShellRunner.DefaultTimeoutMs)
        {
            Require();
            return Record.Exception(() => PowerShellRunner.Run(cmd, timeoutMs));
        }

        [Fact] public void ComandoCorrecto_NoFalla() => RunOk("Write-Output 'hola'");

        [Fact]
        public void ErrorSilenciadoAlFinal_NoEsFalsoPositivo() =>
            RunOk("Write-Output 'x'; Get-Item '/no/existe/sothe' -ErrorAction SilentlyContinue");

        [Fact]
        public void ErrorNoSilenciado_Falla()
        {
            var ex = RunFail("Get-Item '/no/existe/sothe'; Write-Output 'sigue'");
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("existe", ex.Message);
        }

        [Fact]
        public void ExitExplicito_Falla()
        {
            var ex = RunFail("exit 3");
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("code 3", ex.Message);
        }

        [Fact]
        public void ComandoNativoQueFalla_Falla()
        {
            var ex = RunFail(OperatingSystem.IsWindows() ? "cmd.exe /c exit 4" : "/bin/sh -c 'exit 4'");
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("code 4", ex.Message);
        }

        [Fact]
        public void ExcepcionLanzada_Falla() => Assert.IsType<InvalidOperationException>(RunFail("throw 'roto'"));

        [Fact]
        public void ComillasYBarras_LleganIntactas() =>
            RunOk("if (\"C:\\Temp\\\" -ne 'C:\\Temp\\') { exit 9 }; if ('a \"b\" c' -ne \"a `\"b`\" c\") { exit 8 }");

        [Fact]
        public void ComandoColgado_SeCancelaPorTimeout() => Assert.IsType<TimeoutException>(RunFail("Start-Sleep -Seconds 20", 1500));

        [Fact]
        public void ClixmlSoloProgreso_NoEsError() =>
            Assert.Equal("", PowerShellRunner.CleanStdErr("#< CLIXML\r\n<Objs Version=\"1.1.0.1\"><Obj S=\"progress\" RefId=\"0\"><TN RefId=\"0\"><T>System.Management.Automation.PSCustomObject</T></TN><MS><I64 N=\"SourceId\">1</I64></MS></Obj></Objs>"));

        [Fact]
        public void ClixmlConError_DevuelveMensaje() =>
            Assert.Equal("Acceso denegado", PowerShellRunner.CleanStdErr("#< CLIXML\r\n<Objs Version=\"1.1.0.1\"><S S=\"Error\">Acceso denegado_x000D__x000A_</S></Objs>"));
    }
}
