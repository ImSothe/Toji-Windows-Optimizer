using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Toji
{
    // Parte que habla con Windows: procesos de winget, WMI y PowerShell
    [SupportedOSPlatform("windows")]
    public static class WindowsToolsNative
    {
        // null si winget no esta instalado (falta "Instalador de aplicaciones" de Microsoft Store)
        public static async Task<string> WingetVersionAsync(CancellationToken ct = default)
        {
            var (code, output) = await RunAsync("winget", new[] { "--version" }, 15000, ct);
            return code == 0 ? output.Trim() : null;
        }

        // Ids de las apps instaladas que winget conoce (una sola llamada, fiable: JSON en vez de tabla)
        public static async Task<HashSet<string>> WingetInstalledAsync(CancellationToken ct = default)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "toji-winget-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await RunAsync("winget", new[] { "export", "-o", tmp, "--accept-source-agreements" }, 120000, ct);
                // Si alguna app no esta en los origenes winget devuelve error pero escribe el fichero igualmente
                return File.Exists(tmp) ? Winget.ParseExport(await File.ReadAllTextAsync(tmp, ct)) : new HashSet<string>();
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        // WMI es instantaneo; si falla (WMI roto o sin .NET Framework 4) se pregunta a PowerShell
        public static Dictionary<string, FeatureState> QueryFeatures(IEnumerable<string> names)
        {
            var list = names.ToList();
            try { return QueryFeaturesWmi(list); }
            catch { return FeatureCommands.ParseStates(PowerShellRunner.RunCapture(FeatureCommands.QueryScript(list), 120000), list); }
        }

        private static Dictionary<string, FeatureState> QueryFeaturesWmi(IEnumerable<string> names)
        {
            var result = names.Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(n => n, n => FeatureState.Unavailable, StringComparer.OrdinalIgnoreCase);
            using var searcher = new ManagementObjectSearcher("SELECT Name, InstallState FROM Win32_OptionalFeature");
            foreach (ManagementObject o in searcher.Get())
                using (o)
                {
                    var n = o["Name"] as string;
                    if (n != null && result.ContainsKey(n)) result[n] = FeatureCommands.FromInstallState(Convert.ToUInt32(o["InstallState"]));
                }
            return result;
        }

        // true si Windows pide reiniciar. Hyper-V o .NET 3.5 pueden tardar varios minutos
        public static bool SetFeature(string feature, bool enable)
        {
            var output = PowerShellRunner.RunCapture(FeatureCommands.SetScript(feature, enable), 30 * 60 * 1000);
            return output.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<string> GpuNames()
        {
            var names = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject o in searcher.Get())
                    using (o) { var n = o["Name"] as string; if (!string.IsNullOrWhiteSpace(n)) names.Add(n.Trim()); }
            }
            catch { }
            return names;
        }

        private static async Task<(int code, string output)> RunAsync(string exe, IEnumerable<string> args, int timeoutMs, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            try
            {
                using var p = Process.Start(psi);
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-1, ""); }
                await stderr;
                return (p.ExitCode, await stdout);
            }
            catch (System.ComponentModel.Win32Exception) { return (-1, ""); }   // no instalado
        }
    }
}
