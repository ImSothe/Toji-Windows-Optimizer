using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Sothe.Tests
{
    // Un revertido que falla no debe borrar el backup (si no, la app lo daria por revertido y no se podria reintentar)
    public class RevertTests
    {
        private static SotheEngine EngineWithCommandBackup(TestDirs d, string revertCommand)
        {
            TestPowerShell.Use();
            var eng = d.Engine();
            var backup = new[] { new OpBackup { Type = "command", Revert = revertCommand } };
            File.WriteAllText(Path.Combine(eng.BackupDir, "tweak_prueba.json"), JsonSerializer.Serialize(backup));
            return eng;
        }

        [Fact]
        public void RevertidoCorrecto_BorraElBackup()
        {
            using var d = new TestDirs();
            var eng = EngineWithCommandBackup(d, "Write-Output 'ok'");
            var r = eng.RevertTweak("tweak_prueba");
            Assert.True(r.Ok);
            Assert.DoesNotContain("tweak_prueba", eng.GetApplied());
        }

        [Fact]
        public void RevertidoFallido_ConservaElBackup_YRegistraElError()
        {
            using var d = new TestDirs();
            var eng = EngineWithCommandBackup(d, "exit 5");
            var r = eng.RevertTweak("tweak_prueba");
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("código 5"));
            Assert.Contains("tweak_prueba", eng.GetApplied());
            Assert.Contains("REVERTIDO CON ERRORES tweak_prueba", File.ReadAllText(Path.Combine(eng.Root, "sothe.log")));
        }
    }
}
