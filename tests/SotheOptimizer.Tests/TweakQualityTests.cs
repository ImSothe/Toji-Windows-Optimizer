using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using Xunit;

namespace Sothe.Tests
{
    // Reglas de calidad que cumplen TODOS los ajustes de tweaks.json y comprobaciones de los ajustes mas delicados.
    // Son pruebas de datos: no ejecutan los scripts ni tocan el sistema.
    public class TweakQualityTests
    {
        private static List<Tweak> All()
        {
            using var d = new TestDirs();
            return d.Engine().LoadTweaks();
        }

        private static Tweak Get(string id) => All().Single(t => t.Id == id);

        [Fact]
        public void TodoComandoTieneReversion()
        {
            foreach (var t in All())
                foreach (var op in t.Ops.Where(o => o.Type == "command"))
                    Assert.False(string.IsNullOrWhiteSpace(op.Revert), t.Id + ": a command op without a revert script");
        }

        [Fact]
        public void SiLaReversionUsaCaptured_HayCapture()
        {
            foreach (var t in All())
                foreach (var op in t.Ops.Where(o => o.Type == "command" && (o.Revert ?? "").Contains("$captured")))
                    Assert.False(string.IsNullOrWhiteSpace(op.Capture), t.Id + ": revert uses $captured but nothing captures it");
        }

        [Fact]
        public void LosIdsSonUnicos_YTodosTienenTextoEnLosDosIdiomas()
        {
            var all = All();
            Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
            foreach (var t in all)
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Name), t.Id);
                Assert.NotNull(t.Es);
            }
        }

        [Fact]
        public void Ipv4Preferido_ActivaSoloSuBit_YConservaElValorPrevio()
        {
            var op = Get("ipv4_preferred").Ops.Single();
            Assert.Equal("command", op.Type);
            Assert.Contains("-bor 0x20", op.Apply);            // solo el bit 0x20, sin pisar el resto
            Assert.Contains("'absent'", op.Capture);           // distingue "no existia" de "valor 0"
            Assert.Contains("Remove-ItemProperty", op.Revert); // si no existia, se borra al revertir
            Assert.True(Get("ipv4_preferred").Reboot);
        }

        [Fact]
        public void BloqueoDeRazer_SoloTocaSuCarpeta()
        {
            // la busqueda automatica de controladores es global: no puede colarse en un ajuste "solo de Razer"
            var t = Get("razer_block");
            Assert.DoesNotContain(t.Ops, o => o.Type == "reg");
            Assert.Single(t.Ops, o => o.Type == "command");
        }

        [Fact]
        public void BloqueosDeCarpeta_RespetanUnaReglaPrevia()
        {
            foreach (var id in new[] { "razer_block", "logitech_block" })
            {
                var op = Get(id).Ops.Single(o => o.Type == "command");
                Assert.Contains("'denied'", op.Capture);          // ya estaba denegado: no se quita al revertir
                Assert.Contains("-ne 'denied'", op.Revert);
            }
        }

        [Fact]
        public void OneDrive_SoloActuaSiEstaInstalado_YFallaSiSigueAhi()
        {
            var op = Get("onedrive_remove").Ops.Single();
            Assert.DoesNotContain("OneDriveSetup", op.Capture);      // el instalador siempre esta en System32: no prueba nada
            Assert.Contains("OneDrive.exe", op.Capture);
            Assert.Contains("-eq 'installed'", op.Revert);           // sin OneDrive de partida, revertir no lo instala
            Assert.Contains("throw", op.Apply);
        }

        [Fact]
        public void Edge_FallaSiSigueInstalado_YConservaElPerfil()
        {
            var op = Get("edge_remove").Ops.Single();
            Assert.Contains("throw", op.Apply);
            Assert.DoesNotContain("--delete-profile", op.Apply);
            Assert.Contains("-eq 'installed'", op.Revert);
        }

        [Fact]
        public void AtajosDeAccesibilidad_CubrenLasTresFunciones()
        {
            var ops = Get("sticky_keys_off").Ops;
            Assert.Contains(ops, o => o.Path.EndsWith("StickyKeys"));
            Assert.Contains(ops, o => o.Path.EndsWith("Keyboard Response"));
            Assert.Contains(ops, o => o.Path.EndsWith("ToggleKeys"));
            Assert.All(ops, o => Assert.Equal("String", o.Kind));    // en el registro estas Flags son REG_SZ
        }

        // Bug real encontrado en el banco de pruebas: un servicio que Windows no tiene (p. ej. CscService) hacia que el motor
        // creara una clave vacia bajo Services que la reversion no podia quitar.
        [Fact]
        public void ServicioInexistente_SeOmite_SinCrearClaves()
        {
            const string name = "TojiNoSuchService";
            using var d = new TestDirs();
            var eng = d.Engine();
            var t = new Tweak { Id = "svc_missing", Name = "svc_missing", Ops = new List<TweakOp> { new TweakOp { Type = "service", Name = name, Startup = "Disabled" } } };

            var r = eng.ApplyTweak(t);

            Assert.True(r.Ok);
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            Assert.Null(key);
        }
    }
}
