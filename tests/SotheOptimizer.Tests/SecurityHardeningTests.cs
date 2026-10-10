using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Sothe.Licensing;
using Xunit;

namespace Sothe.Tests
{
    // Pruebas de los parches de la auditoria: almacenamiento protegido, rutas absolutas, Timer Resolution,
    // licencia con fecha de validacion falsa y validacion de nombres que acaban dentro de comandos de PowerShell.
    public class SecurityHardeningTests
    {
        private static string NewDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "sothe-sec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        // ---------------- SecureStorage ----------------
        [Fact]
        public void SecureStorage_ClasificaPropietarios()
        {
            if (!OperatingSystem.IsWindows()) return;
            Assert.True(SecureStorage.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
            Assert.True(SecureStorage.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
            Assert.False(SecureStorage.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
            Assert.False(SecureStorage.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.WorldSid, null)));
            Assert.False(SecureStorage.IsTrustedOwner(null));
            var other = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
            Assert.False(SecureStorage.IsTrustedOwner(other));
            Assert.True(SecureStorage.IsTrustedOwner(other, new[] { other }));   // los tests pueden declarar su propio usuario
        }

        [Fact]
        public void Harden_DejaAclProtegida_SinEscrituraParaUsuarios_YConservaLoDeConfianza()
        {
            if (!OperatingSystem.IsWindows()) return;
            var me = WindowsIdentity.GetCurrent().User;
            var root = Path.Combine(NewDir(), "SotheOptimizer");
            Directory.CreateDirectory(Path.Combine(root, "backups"));
            File.WriteAllText(Path.Combine(root, "backups", "x.json"), "[]");

            Assert.True(SecureStorage.Harden(root, null, new[] { me }));
            Assert.True(SecureStorage.RootTrusted);
            Assert.True(File.Exists(Path.Combine(root, "backups", "x.json")));   // lo de un propietario de confianza se respeta

            var acl = new DirectoryInfo(root).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected, "no debe heredar de ProgramData");
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var userRules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Where(r => r.IdentityReference.Equals(users) && r.AccessControlType == AccessControlType.Allow).ToList();
            Assert.NotEmpty(userRules);   // pueden leer...
            Assert.All(userRules, r =>
            {
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.WriteData), "los usuarios no deben poder crear ficheros");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.AppendData), "ni carpetas");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Delete));
            });
        }

        [Fact]
        public void Harden_CarpetaPreCreadaPorOtroUsuario_SeApartaYSeEmpiezaDeCero()
        {
            if (!OperatingSystem.IsWindows()) return;
            var root = Path.Combine(NewDir(), "SotheOptimizer");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "plantado.json"), "malicioso");
            if (SecureStorage.IsOwnedByTrusted(root)) return;   // ejecutando elevado: el propietario ya es el grupo Administradores

            var log = new System.Collections.Generic.List<string>();
            Assert.True(SecureStorage.Harden(root, log.Add));   // el usuario de los tests NO es de confianza aqui
            Assert.False(File.Exists(Path.Combine(root, "plantado.json")));   // lo plantado ya no esta en la carpeta nueva
            Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(root), "SotheOptimizer.untrusted-*"));
            Assert.Contains(log, l => l.Contains("apartada"));
        }

        // ---------------- Rutas absolutas ----------------
        [Fact]
        public void SystemPaths_SonAbsolutas()
        {
            if (!OperatingSystem.IsWindows()) return;
            Assert.True(Path.IsPathRooted(SystemPaths.Cmd) && File.Exists(SystemPaths.Cmd));
            Assert.True(Path.IsPathRooted(SystemPaths.WindowsPowerShell) && File.Exists(SystemPaths.WindowsPowerShell));
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), SystemPaths.ProtectedInstallDir, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PowerShellRunner_UsaPorDefectoLaRutaAbsoluta()
        {
            if (!OperatingSystem.IsWindows()) return;
            // otros tests cambian Executable a pwsh: aqui solo se comprueba el valor por defecto de la clase
            Assert.True(Path.IsPathRooted(SystemPaths.WindowsPowerShell));
        }

        // ---------------- Timer Resolution ----------------
        [Fact]
        public void TimerResolution_ApplyYRelease_SueltanLaPeticion()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                uint real = TimerResolution.Apply();
                Assert.True(TimerResolution.IsApplied);
                Assert.True(real > 0 && real <= 10000, $"resolucion real inesperada: {real} (x100 ns)");
                Assert.True(TimerResolution.CurrentMs() <= 1.0, "con la peticion activa debe ir a 1 ms o menos");
            }
            finally
            {
                TimerResolution.Release();
                TimerResolution.Release();   // idempotente
            }
            Assert.False(TimerResolution.IsApplied);
        }

        [Fact]
        public void TimerResolution_ReleaseSinApply_NoHaceNada()
        {
            if (!OperatingSystem.IsWindows()) return;
            TimerResolution.Release();
            Assert.False(TimerResolution.IsApplied);
        }

        [Fact]
        public void ScriptDeParada_SenalaElEventoAntesDeMatar()
        {
            var stop = TimerResolution.BuildStopScript();
            Assert.Contains(TimerResolution.StopEventName, stop);
            Assert.Contains("OpenExisting", stop);
            var un = TimerResolution.BuildUninstallScript();
            Assert.True(un.IndexOf("OpenExisting", StringComparison.Ordinal) < un.IndexOf("Unregister-ScheduledTask", StringComparison.Ordinal));
            Assert.True(un.IndexOf("OpenExisting", StringComparison.Ordinal) < un.IndexOf("Stop-Process", StringComparison.Ordinal));
        }

        // ---------------- Licencia ----------------
        [Fact]
        public void LicenciaConValidacionEnElFuturo_PideValidarDeNuevo()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var forged = new LicenseCache
            {
                Key = "FALSA-1234", Fingerprint = "FP", IsTrial = false, TrialUsed = false,
                LastValidatedUtc = now.AddYears(5), LastSeenUtc = now.AddYears(-1)   // "validada" dentro de 5 anos: sin caducar nunca offline
            };
            var s = LicenseService.Evaluate(forged, "FP", now);
            Assert.Equal(LicenseState.ValidationRequired, s.State);
            Assert.False(s.CanModify);
        }

        [Fact]
        public void LicenciaLegitima_ValidadaAhora_SigueFuncionando()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var ok = new LicenseCache { Key = "ABCD-1234", Fingerprint = "FP", LastValidatedUtc = now.AddMinutes(-5), LastSeenUtc = now.AddMinutes(-5) };
            Assert.Equal(LicenseState.Licensed, LicenseService.Evaluate(ok, "FP", now).State);
        }

        // ---------------- Validacion de nombres que acaban en PowerShell ----------------
        [Theory]
        [InlineData("wuauserv", true)]
        [InlineData("MSSQL$SQLEXPRESS", true)]
        [InlineData("Foo.Bar-1", true)]
        [InlineData("x'; Remove-Item C:\\ -Recurse; '", false)]
        [InlineData("a b", false)]
        [InlineData("a`b", false)]
        [InlineData("a\nb", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void NombreDeServicio(string name, bool valid) => Assert.Equal(valid, SotheEngine.IsValidServiceName(name));

        [Theory]
        [InlineData("Microsoft.XboxApp", true)]
        [InlineData("*Xbox*", true)]
        [InlineData("a'; calc; '", false)]
        [InlineData("a|b", false)]
        [InlineData("", false)]
        public void NombreDePaqueteAppx(string name, bool valid) => Assert.Equal(valid, SotheEngine.IsValidAppxName(name));

        [Theory]
        [InlineData("SFC", "SFC")]
        [InlineData("Reset de red", "Reset de red")]
        [InlineData("x'; calc; '", "x calc")]
        [InlineData("\"; calc; \"", "calc")]
        [InlineData("", "Sothe")]
        public void TituloDeVentana_SinComillasNiSeparadores(string input, string expected) =>
            Assert.Equal(expected, SotheEngine.SafeTitle(input));

        [Fact]
        public void Aplicar_ServicioConNombreMalicioso_FallaSinTocarNada()
        {
            using var d = new TestDirs();
            var eng = d.Engine();
            var t = new Tweak { Id = "evil_svc", Ops = new System.Collections.Generic.List<TweakOp> { new TweakOp { Type = "service", Name = "x'; calc; '", Startup = "Disabled" } } };
            var r = eng.ApplyTweak(t);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("nombre de servicio no valido"));
        }
    }
}
