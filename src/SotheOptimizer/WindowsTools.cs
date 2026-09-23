using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sothe
{
    // Parte pura (sin Windows) de las pestanas Apps y Caracteristicas: se prueba en los tests
    public static class Winget
    {
        // Los ids vienen de apps.json (actualizable por OTA): se validan antes de pasarlos a la consola
        private static readonly Regex IdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$");
        public static bool IsValidId(string id) => id != null && IdPattern.IsMatch(id);

        public const string UpgradeAllScript = "winget upgrade --all --accept-source-agreements --accept-package-agreements";
        public static string InstallScript(IEnumerable<string> ids) =>
            Script(ids, id => $"winget install --id {id} -e --accept-source-agreements --accept-package-agreements");
        public static string UninstallScript(IEnumerable<string> ids) => Script(ids, id => $"winget uninstall --id {id} -e");

        private static string Script(IEnumerable<string> ids, Func<string, string> command)
        {
            var list = (ids ?? Enumerable.Empty<string>()).ToList();
            var bad = list.FirstOrDefault(i => !IsValidId(i));
            if (bad != null) throw new ArgumentException("Id de winget no válido: " + bad);
            return string.Join("; ", list.Select(command));
        }

        // "winget export" -> { "Sources": [ { "Packages": [ { "PackageIdentifier": "Valve.Steam" } ] } ] }
        public static HashSet<string> ParseExport(string json)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return ids;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array) return ids;
            foreach (var src in sources.EnumerateArray())
                if (src.TryGetProperty("Packages", out var pkgs) && pkgs.ValueKind == JsonValueKind.Array)
                    foreach (var p in pkgs.EnumerateArray())
                        if (p.TryGetProperty("PackageIdentifier", out var id) && id.ValueKind == JsonValueKind.String)
                            ids.Add(id.GetString());
            return ids;
        }
    }

    public enum FeatureState { Unknown, Enabled, Disabled, Unavailable }

    public static class FeatureCommands
    {
        private static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");
        public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);

        // Devuelve True/False en la salida segun si Windows pide reiniciar
        public static string SetScript(string feature, bool enable)
        {
            if (!IsValidName(feature)) throw new ArgumentException("Característica no válida: " + feature);
            return enable
                ? $"(Enable-WindowsOptionalFeature -Online -FeatureName '{feature}' -All -NoRestart -ErrorAction Stop).RestartNeeded"
                : $"(Disable-WindowsOptionalFeature -Online -FeatureName '{feature}' -NoRestart -ErrorAction Stop).RestartNeeded";
        }

        // Plan B si WMI no esta disponible: una sola llamada a PowerShell que imprime "Nombre=Estado"
        public static string QueryScript(IEnumerable<string> features)
        {
            var list = features.ToList();
            var bad = list.FirstOrDefault(f => !IsValidName(f));
            if (bad != null) throw new ArgumentException("Característica no válida: " + bad);
            return "$n = @(" + string.Join(",", list.Select(f => "'" + f + "'")) + "); " +
                   "Get-WindowsOptionalFeature -Online | Where-Object { $n -contains $_.FeatureName } | ForEach-Object { \"$($_.FeatureName)=$($_.State)\" }";
        }

        public static Dictionary<string, FeatureState> ParseStates(string output, IEnumerable<string> features)
        {
            var result = features.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(f => f, f => FeatureState.Unavailable, StringComparer.OrdinalIgnoreCase);
            foreach (var line in (output ?? "").Split('\n'))
            {
                var parts = line.Trim().Split('=');
                if (parts.Length != 2 || !result.ContainsKey(parts[0])) continue;
                result[parts[0]] = parts[1] switch
                {
                    "Enabled" or "EnablePending" => FeatureState.Enabled,
                    "Disabled" or "DisabledWithPayloadRemoved" or "DisablePending" => FeatureState.Disabled,
                    _ => FeatureState.Unknown
                };
            }
            return result;
        }

        // Win32_OptionalFeature.InstallState: 1 activada, 2 desactivada, 3 ausente
        public static FeatureState FromInstallState(uint state) => state switch
        {
            1 => FeatureState.Enabled,
            2 => FeatureState.Disabled,
            3 => FeatureState.Unavailable,
            _ => FeatureState.Unknown
        };
    }

    public static class GpuDrivers
    {
        private static readonly Regex Ignored = new Regex("Basic|Remote|Microsoft|Virtual|Parsec|Citrix", RegexOptions.IgnoreCase);

        // Primera GPU real (se ignoran adaptadores basicos o virtuales). Prioriza NVIDIA/AMD sobre la integrada.
        public static string PickGpu(IEnumerable<string> names)
        {
            var real = (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n) && !Ignored.IsMatch(n)).ToList();
            return real.FirstOrDefault(n => Regex.IsMatch(n, "NVIDIA|AMD|Radeon", RegexOptions.IgnoreCase)) ?? real.FirstOrDefault();
        }

        // winget no instala drivers de GPU (politica de los fabricantes): se abre la web oficial
        public static (string vendor, string url) DriverPage(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return (null, null);
            if (Regex.IsMatch(gpu, "NVIDIA", RegexOptions.IgnoreCase)) return ("NVIDIA", "https://www.nvidia.com/en-us/software/nvidia-app/");
            if (Regex.IsMatch(gpu, "AMD|Radeon", RegexOptions.IgnoreCase)) return ("AMD", "https://www.amd.com/en/support/download/drivers.html");
            if (Regex.IsMatch(gpu, "Intel", RegexOptions.IgnoreCase)) return ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html");
            return (null, null);
        }
    }
}
