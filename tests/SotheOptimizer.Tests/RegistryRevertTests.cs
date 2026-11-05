using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using Xunit;

namespace Sothe.Tests
{
    // Ida y vuelta REAL contra el registro, pero solo en HKCU\Software\TojiTests\<guid>: nunca se tocan claves del sistema.
    // Estas pruebas cubren lo que la auditoria encontro roto: el orden de reversion (LIFO), reaplicar un ajuste ya
    // aplicado, y que la copia de seguridad exista ANTES de ejecutar cada operacion.
    public sealed class RegistryRevertTests : IDisposable
    {
        private readonly string _sub = @"Software\TojiTests\" + Guid.NewGuid().ToString("N");
        private string KeyPath => @"HKCU:\" + _sub;
        private readonly TestDirs _dirs = new TestDirs();

        public RegistryRevertTests()
        {
            Assert.True(OperatingSystem.IsWindows(), "Estas pruebas necesitan el registro de Windows");
        }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, false); } catch { }
            _dirs.Dispose();
        }

        private static TweakOp RegOp(string path, string name, int value, string kind = null) =>
            new TweakOp { Type = "reg", Path = path, Name = name, Kind = kind, Value = JsonDocument.Parse(value.ToString()).RootElement.Clone() };

        private Tweak Tweak(string id, params TweakOp[] ops) => new Tweak { Id = id, Name = id, Ops = ops.ToList() };

        private int? Read(string name = "Flag")
        {
            using var k = Registry.CurrentUser.OpenSubKey(_sub);
            return k?.GetValue(name) as int?;
        }
        private void Write(string name, int value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(_sub);
            k.SetValue(name, value, RegistryValueKind.DWord);
        }

        [Fact]
        public void IdaYVuelta_RestauraElValorPrevio()
        {
            Write("Flag", 7);
            var eng = _dirs.Engine();
            Assert.True(eng.ApplyTweak(Tweak("solo", RegOp(KeyPath, "Flag", 1))).Ok);
            Assert.Equal(1, Read());
            Assert.True(eng.RevertTweak("solo").Ok);
            Assert.Equal(7, Read());
            Assert.DoesNotContain("solo", eng.GetApplied());
        }

        [Fact]
        public void IdaYVuelta_SiNoExistia_BorraElValor()
        {
            var eng = _dirs.Engine();
            eng.ApplyTweak(Tweak("nuevo", RegOp(KeyPath, "Flag", 1)));
            Assert.Equal(1, Read());
            eng.RevertTweak("nuevo");
            Assert.Null(Read());
        }

        // Mismo escenario que defender_full_off + defender_realtime_off: ambos escriben la misma clave
        [Fact]
        public void DosAjustesSobreLaMismaClave_RevertirTodo_DejaElEstadoOriginal()
        {
            var eng = _dirs.Engine();
            eng.ApplyTweak(Tweak("full_off", RegOp(KeyPath, "Flag", 1)));       // previo: no existia
            eng.ApplyTweak(Tweak("realtime_off", RegOp(KeyPath, "Flag", 1)));   // previo: 1 (lo dejo full_off)

            foreach (var id in eng.AppliedNewestFirst()) Assert.True(eng.RevertTweak(id).Ok);

            Assert.Null(Read());   // con orden alfabetico se quedaba en 1
            Assert.Empty(eng.GetApplied());
        }

        [Fact]
        public void DosAjustesSobreLaMismaClave_EnElOtroOrdenAlfabetico_TambienVuelveAlOriginal()
        {
            Write("Flag", 5);
            var eng = _dirs.Engine();
            eng.ApplyTweak(Tweak("zeta", RegOp(KeyPath, "Flag", 1)));
            eng.ApplyTweak(Tweak("alfa", RegOp(KeyPath, "Flag", 2)));
            foreach (var id in eng.AppliedNewestFirst()) eng.RevertTweak(id);
            Assert.Equal(5, Read());
        }

        [Fact]
        public void OrderForRevert_EsElInversoDelOrdenDeAplicacion()
        {
            var eng = _dirs.Engine();
            foreach (var id in new[] { "c", "a", "d", "b" }) eng.ApplyTweak(Tweak(id, RegOp(KeyPath, "K" + id, 1)));
            Assert.Equal(new[] { "b", "d", "a", "c" }, eng.AppliedNewestFirst());
            Assert.Equal(new[] { "b", "a" }, eng.OrderForRevert(new[] { "a", "b" }));
        }

        [Fact]
        public void DentroDeUnAjuste_LasOperacionesSobreLaMismaClaveSeDeshacenAlReves()
        {
            Write("Flag", 7);
            var eng = _dirs.Engine();
            eng.ApplyTweak(Tweak("doble", RegOp(KeyPath, "Flag", 1), RegOp(KeyPath, "Flag", 2)));
            Assert.Equal(2, Read());
            eng.RevertTweak("doble");
            Assert.Equal(7, Read());   // en orden directo quedaba en 1
        }

        [Fact]
        public void ReaplicarUnAjusteYaAplicado_NoPierdeElEstadoOriginal()
        {
            Write("Flag", 7);
            var eng = _dirs.Engine();
            var t = Tweak("x", RegOp(KeyPath, "Flag", 1));
            Assert.False(eng.ApplyTweak(t).Skipped);
            var second = eng.ApplyTweak(t);
            Assert.True(second.Ok);
            Assert.True(second.Skipped);
            eng.RevertTweak("x");
            Assert.Equal(7, Read());
        }

        [Fact]
        public void LaCopiaDeSeguridadExisteAntesDeEjecutarUnComando()
        {
            TestPowerShell.Use();
            var eng = _dirs.Engine();
            var backup = Path.Combine(eng.BackupDir, "wal.json");
            // El comando solo termina bien si el backup ya esta en disco cuando se ejecuta
            var op = new TweakOp { Type = "command", Apply = $"if (-not (Test-Path -LiteralPath '{backup}')) {{ exit 3 }}", Revert = "exit 0" };
            var r = eng.ApplyTweak(Tweak("wal", op));
            Assert.True(r.Ok, string.Join("; ", r.Errors));
        }

        [Fact]
        public void SiUnaOperacionFalla_ElBackupConservaLasAnteriores_YSePuedeRevertir()
        {
            TestPowerShell.Use();
            Write("Flag", 7);
            var eng = _dirs.Engine();
            var t = Tweak("parcial", RegOp(KeyPath, "Flag", 1), new TweakOp { Type = "command", Apply = "exit 5", Revert = "exit 0" });
            var r = eng.ApplyTweak(t);
            Assert.False(r.Ok);
            Assert.Contains("parcial", eng.GetApplied());
            Assert.True(eng.RevertTweak("parcial").Ok);
            Assert.Equal(7, Read());
        }

        [Fact]
        public void UnBackupConFormatoAntiguo_SeSigueRevirtiendo()
        {
            Write("Flag", 1);
            var eng = _dirs.Engine();
            var legacy = new[] { new OpBackup { Type = "reg", Path = KeyPath, Name = "Flag", PriorExists = true, PriorValue = "7", PriorKind = "DWord" } };
            File.WriteAllText(Path.Combine(eng.BackupDir, "viejo.json"), JsonSerializer.Serialize(legacy));
            Assert.True(eng.RevertTweak("viejo").Ok);
            Assert.Equal(7, Read());
        }

        [Fact]
        public void UnBackupIlegible_NoLanza_SeConservaYSeAvisa()
        {
            var eng = _dirs.Engine();
            File.WriteAllText(Path.Combine(eng.BackupDir, "roto.json"), "{ esto no es json");
            var r = eng.RevertTweak("roto");
            Assert.False(r.Ok);
            Assert.Contains("roto", eng.GetApplied());
        }

        [Fact]
        public void UnAjusteSinOperaciones_NoDejaBackup()
        {
            var eng = _dirs.Engine();
            eng.ApplyTweak(new Tweak { Id = "vacio", Ops = new List<TweakOp>() });
            Assert.DoesNotContain("vacio", eng.GetApplied());
        }
    }
}
