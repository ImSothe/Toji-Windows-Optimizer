using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using Xunit;

namespace Sothe.Tests
{
    // Reversion al estado REAL previo en los ajustes de comando: "capture" guarda lo que habia y "$captured" lo devuelve
    // a apply/revert. Antes se restauraban valores fijos (Equilibrado, MSISupported=0...) aunque el usuario tuviera otros.
    public sealed class CaptureRevertTests : IDisposable
    {
        private readonly TestDirs _dirs = new TestDirs();
        public CaptureRevertTests() { TestPowerShell.Use(); }
        public void Dispose() => _dirs.Dispose();

        private static string Q(string path) => path.Replace("'", "''");

        [Fact]
        public void WithCaptured_PasaElValorComoCadenaEntreComillas_NuncaComoCodigo()
        {
            Assert.Equal("exit 0", SotheEngine.WithCaptured(null, "exit 0"));
            var s = SotheEngine.WithCaptured("a'; calc; '", "Write-Output $captured");
            Assert.StartsWith("$captured = 'a''; calc; ''';", s);   // la comilla va duplicada: sigue siendo una sola cadena
        }

        [Fact]
        public void ElEstadoCapturado_LlegaAApplyYARevert_IntactoAunConComillas()
        {
            var eng = _dirs.Engine();
            var applied = Path.Combine(_dirs.Base, "applied.txt");
            var reverted = Path.Combine(_dirs.Base, "reverted.txt");
            const string state = "it's|a b";   // contiene una comilla simple y una barra
            var t = new Tweak { Id = "cap", Ops = new List<TweakOp> { new TweakOp {
                Type = "command",
                Capture = $"Write-Output \"{state.Replace("'", "'")}\"",
                Apply = $"Set-Content -LiteralPath '{Q(applied)}' -Value $captured -NoNewline",
                Revert = $"Set-Content -LiteralPath '{Q(reverted)}' -Value $captured -NoNewline" } } };

            Assert.True(eng.ApplyTweak(t).Ok);
            Assert.Equal(state, File.ReadAllText(applied));
            Assert.True(eng.RevertTweak("cap").Ok);
            Assert.Equal(state, File.ReadAllText(reverted));
        }

        [Fact]
        public void SiLaCapturaFalla_NoSeAplicaNada_YElAjusteNoCuentaComoAplicado()
        {
            var eng = _dirs.Engine();
            var marker = Path.Combine(_dirs.Base, "ran.txt");
            var t = new Tweak { Id = "nocap", Ops = new List<TweakOp> { new TweakOp {
                Type = "command", Capture = "exit 4",
                Apply = $"Set-Content -LiteralPath '{Q(marker)}' -Value x", Revert = "exit 0" } } };
            var r = eng.ApplyTweak(t);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("could not save the previous state"));
            Assert.False(File.Exists(marker));                       // sin estado previo no hay forma de volver: no se toca
            Assert.DoesNotContain("nocap", eng.GetApplied());
        }

        [Fact]
        public void UnComandoSinCapture_SigueFuncionandoComoAntes()
        {
            var eng = _dirs.Engine();
            var t = new Tweak { Id = "plain", Ops = new List<TweakOp> { new TweakOp { Type = "command", Apply = "exit 0", Revert = "exit 0" } } };
            Assert.True(eng.ApplyTweak(t).Ok);
            Assert.True(eng.RevertTweak("plain").Ok);
        }
    }

    // regsub: el mismo valor en cada subclave (una por interfaz de red en Nagle) con su valor previo por separado
    public sealed class RegSubTests : IDisposable
    {
        private readonly string _sub = @"Software\TojiTests\" + Guid.NewGuid().ToString("N");
        private readonly TestDirs _dirs = new TestDirs();
        public void Dispose() { try { Registry.CurrentUser.DeleteSubKeyTree(_sub, false); } catch { } _dirs.Dispose(); }

        private int? Read(string child, string name)
        {
            using var k = Registry.CurrentUser.OpenSubKey(_sub + "\\" + child);
            return k?.GetValue(name) as int?;
        }

        [Fact]
        public void CadaSubclaveVuelveASuValorPrevio_ElQueNoTeniaNadaSeQuedaSinEl()
        {
            using (var a = Registry.CurrentUser.CreateSubKey(_sub + "\\A")) a.SetValue("V", 7, RegistryValueKind.DWord);   // ya tenia 7
            using (var b = Registry.CurrentUser.CreateSubKey(_sub + "\\B")) b.SetValue("V", 1, RegistryValueKind.DWord);   // ya estaba a 1
            using (Registry.CurrentUser.CreateSubKey(_sub + "\\C")) { }                                                       // sin valor
            var eng = _dirs.Engine();
            var t = new Tweak { Id = "sub", Ops = new List<TweakOp> { new TweakOp {
                Type = "regsub", Path = @"HKCU:\" + _sub, Name = "V", Value = JsonDocument.Parse("1").RootElement.Clone() } } };

            Assert.True(eng.ApplyTweak(t).Ok);
            Assert.Equal(1, Read("A", "V")); Assert.Equal(1, Read("B", "V")); Assert.Equal(1, Read("C", "V"));

            Assert.True(eng.RevertTweak("sub").Ok);
            Assert.Equal(7, Read("A", "V"));
            Assert.Equal(1, Read("B", "V"));     // antes de este cambio, revertir borraba valores que el usuario ya tenia
            Assert.Null(Read("C", "V"));
        }

        [Fact]
        public void SinSubclaves_NoHaceNadaYNoDejaBackup()
        {
            Registry.CurrentUser.CreateSubKey(_sub).Dispose();
            var eng = _dirs.Engine();
            var t = new Tweak { Id = "vacio", Ops = new List<TweakOp> { new TweakOp {
                Type = "regsub", Path = @"HKCU:\" + _sub, Name = "V", Value = JsonDocument.Parse("1").RootElement.Clone() } } };
            Assert.True(eng.ApplyTweak(t).Ok);
            Assert.DoesNotContain("vacio", eng.GetApplied());
        }
    }

    // Reglas del catalogo: toda operacion de comando se puede deshacer y los scripts son PowerShell valido
    public class CatalogReversibilityTests
    {
        private static List<Tweak> Tweaks()
        {
            using var d = new TestDirs();
            return d.Engine().LoadTweaks();
        }

        [Fact]
        public void TodoComandoTieneRevert()
        {
            foreach (var t in Tweaks())
                foreach (var op in t.Ops.Where(o => o.Type == "command"))
                    Assert.False(string.IsNullOrWhiteSpace(op.Revert), $"{t.Id}: command sin revert");
        }

        [Fact]
        public void SiUnComandoCaptura_ElRevertUsaLoCapturado()
        {
            foreach (var t in Tweaks())
                foreach (var op in t.Ops.Where(o => o.Type == "command" && !string.IsNullOrEmpty(o.Capture)))
                    Assert.True(op.Revert.Contains("$captured"), $"{t.Id}: captura el estado pero el revert no lo usa");
        }

        [Fact]
        public void NingunRevertRestauraUnValorFijoDePlanDeEnergia()
        {
            foreach (var t in Tweaks().Where(x => x.Ops.Any(o => o.Command().Contains("powercfg"))))
                foreach (var op in t.Ops.Where(o => o.Type == "command"))
                    if (op.Apply.Contains("powercfg") && !op.Apply.Contains("/hibernate"))
                        Assert.False(string.IsNullOrEmpty(op.Capture), $"{t.Id}: toca powercfg sin capturar el estado previo");
        }

        [Fact]
        public void TodosLosScriptsSonPowerShellValido()
        {
            TestPowerShell.Use();
            var dir = Path.Combine(Path.GetTempPath(), "toji-parse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                int n = 0;
                foreach (var t in Tweaks())
                    foreach (var op in t.Ops.Where(o => o.Type == "command"))
                        foreach (var (kind, s) in new[] { ("apply", op.Apply), ("revert", op.Revert), ("capture", op.Capture) })
                            if (!string.IsNullOrEmpty(s) && s != TimerResolution.InstallSentinel && s != TimerResolution.UninstallSentinel)
                                File.WriteAllText(Path.Combine(dir, $"{t.Id}.{kind}.{n++}.ps1"), s);
                var script = "$bad = @(); foreach ($f in Get-ChildItem -LiteralPath '" + Q(dir) + "' -Filter *.ps1) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e); if ($e) { $bad += $f.Name + ': ' + $e[0].Message } }; if ($bad) { throw ($bad -join '; ') }";
                PowerShellRunner.Run(script);   // lanza si algun script no parsea
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static string Q(string p) => p.Replace("'", "''");
    }

    internal static class OpExt
    {
        public static string Command(this TweakOp o) => (o.Apply ?? "") + (o.Revert ?? "") + (o.Capture ?? "");
    }
}
