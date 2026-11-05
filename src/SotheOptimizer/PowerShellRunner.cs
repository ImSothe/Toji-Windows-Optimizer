using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sothe
{
    // Ejecuta PowerShell oculto y DETECTA los fallos, en lugar de dar el comando por bueno:
    //  - codigo de salida distinto de 0 (errores que abortan, 'exit N', o un comando nativo que falla al final)
    //  - errores escritos en stderr (errores no silenciados con -ErrorAction SilentlyContinue)
    // Los errores silenciados a proposito en tweaks.json no cuentan como fallo.
    public static class PowerShellRunner
    {
        // Ruta absoluta de Windows PowerShell 5.1 (un nombre suelto se buscaria primero junto al .exe: ver SystemPaths).
        // Los tests la cambian a "pwsh" si esta instalado.
        public static string Executable = SystemPaths.WindowsPowerShell;
        public const int DefaultTimeoutMs = 5 * 60 * 1000;

        public static void Run(string command, int timeoutMs = DefaultTimeoutMs) => RunCapture(command, timeoutMs);

        // Igual que Run, pero devuelve la salida estandar
        public static string RunCapture(string command, int timeoutMs = DefaultTimeoutMs)
        {
            var psi = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // ArgumentList escapa comillas y barras correctamente (el Replace("\"", "\\\"") anterior no)
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Wrap(command) })
                psi.ArgumentList.Add(a);

            Process p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) { throw new InvalidOperationException("Could not start PowerShell: " + ex.Message, ex); }

            using (p)
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    throw new TimeoutException($"PowerShell did not finish in {timeoutMs / 1000} s and was cancelled.");
                }
                p.WaitForExit();   // vacia los lectores asincronos
                var err = CleanStdErr(stderr.Result);
                if (p.ExitCode != 0 || err.Length > 0)
                    throw new InvalidOperationException($"PowerShell failed (code {p.ExitCode})" + (err.Length > 0 ? ": " + Truncate(err, 400) : "."));
                return stdout.Result ?? "";
            }
        }

        // El 'exit 0' final evita falsos fallos: sin el, un ultimo comando con -ErrorAction SilentlyContinue
        // que tuviera un error silenciado devolveria codigo 1 aunque todo este bien.
        internal static string Wrap(string command) =>
            "$ProgressPreference = 'SilentlyContinue'\n" +
            "try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }\n" +
            command + "\n" +
            "if ($LASTEXITCODE) { exit $LASTEXITCODE }\n" +
            "exit 0";

        // PowerShell 5.1 a veces serializa en stderr registros de progreso en CLIXML: solo interesan los de error
        internal static string CleanStdErr(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            if (!s.StartsWith("#< CLIXML", StringComparison.Ordinal)) return s;
            var msgs = Regex.Matches(s, "<S S=\"Error\">(.*?)</S>", RegexOptions.Singleline)
                .Cast<Match>()
                .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D_", "").Replace("_x000A_", " ").Trim())
                .Where(m => m.Length > 0);
            return string.Join(" ", msgs).Trim();
        }

        private static string Truncate(string s, int max)
        {
            s = Regex.Replace(s, @"\s+", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
