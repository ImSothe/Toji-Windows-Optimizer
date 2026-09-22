using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Toji
{
    public class TojiEngine
    {
        public readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TOJI");
        public readonly string BackupDir;
        public readonly string BaseDir;               // carpeta del .exe (donde estan los JSON)
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

        public TojiEngine()
        {
            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            BackupDir = Path.Combine(Root, "backups");
            Directory.CreateDirectory(BackupDir);
        }

        private string DataPath(string file)
        {
            // Busca los JSON en \data\ y, si no, junto al exe (compatibilidad)
            var p1 = Path.Combine(BaseDir, "data", file);
            if (File.Exists(p1)) return p1;
            return Path.Combine(BaseDir, file);
        }

        public void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Root, "toji.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); }
            catch { }
        }

        // ---------------- Carga de datos ----------------
        public List<Tweak> LoadTweaks()
        {
            var f = JsonSerializer.Deserialize<TweaksFile>(File.ReadAllText(DataPath("tweaks.json")), _json);
            return f?.Tweaks ?? new List<Tweak>();
        }
        public List<Profile> LoadProfiles()
        {
            var path = DataPath("profiles.json");
            if (!File.Exists(path)) return new List<Profile>();
            var f = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(path), _json);
            return f?.Profiles ?? new List<Profile>();
        }

        // ---------------- Estado aplicado ----------------
        public HashSet<string> GetApplied()
        {
            return new HashSet<string>(Directory.GetFiles(BackupDir, "*.json").Select(Path.GetFileNameWithoutExtension));
        }
        private string BackupPath(string id) => Path.Combine(BackupDir, id + ".json");

        // ---------------- Registro ----------------
        private static (RegistryKey baseKey, string sub) ParsePath(string path)
        {
            var clean = path.Replace(":", "");
            int i = clean.IndexOf('\\');
            string hive = clean.Substring(0, i).ToUpperInvariant();
            string sub = clean.Substring(i + 1);
            RegistryKey bk;
            switch (hive)
            {
                case "HKLM": bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); break;
                case "HKCU": bk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64); break;
                case "HKCR": bk = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64); break;
                case "HKU": bk = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64); break;
                default: throw new Exception("Hive no reconocido: " + hive);
            }
            return (bk, sub);
        }

        private void RegSet(string path, string name, object value, RegistryValueKind kind)
        {
            var (bk, sub) = ParsePath(path);
            using (bk)
            using (var key = bk.CreateSubKey(sub))
                key.SetValue(name, value, kind);
        }
        private void RegDelete(string path, string name)
        {
            var (bk, sub) = ParsePath(path);
            using (bk)
            using (var key = bk.OpenSubKey(sub, true))
                if (key != null && key.GetValueNames().Contains(name)) key.DeleteValue(name, false);
        }
        private (bool exists, string val, string kind) RegGet(string path, string name)
        {
            var (bk, sub) = ParsePath(path);
            using (bk)
            using (var key = bk.OpenSubKey(sub, false))
            {
                if (key == null || !key.GetValueNames().Contains(name)) return (false, null, null);
                var v = key.GetValue(name);
                var k = key.GetValueKind(name);
                return (true, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), k.ToString());
            }
        }

        private static object ConvertValue(TweakOp op, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return unchecked((int)op.Value.GetInt64());
                case RegistryValueKind.QWord: return op.Value.GetInt64();
                default: // String / ExpandString
                    return op.Value.ValueKind == JsonValueKind.String ? op.Value.GetString() : op.Value.ToString();
            }
        }
        private static object ConvertStored(string val, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return unchecked((int)long.Parse(val));
                case RegistryValueKind.QWord: return long.Parse(val);
                default: return val;
            }
        }

        // ---------------- Servicios (via registro Start) ----------------
        private static readonly Dictionary<string, int> StartMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { { "Boot", 0 }, { "System", 1 }, { "Automatic", 2 }, { "Auto", 2 }, { "Manual", 3 }, { "Disabled", 4 } };

        // ---------------- PowerShell (command / appx / restore) ----------------
        public void RunPowerShellHidden(string command)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (var p = Process.Start(psi)) p.WaitForExit();
        }
        public void RunPowerShellVisible(string title, string command)
        {
            var full = "$host.UI.RawUI.WindowTitle='" + title + "'; Write-Host '=== " + title + " ===' -ForegroundColor Green; " + command + "; Write-Host ''; Write-Host 'Terminado. Puedes cerrar esta ventana.' -ForegroundColor Green";
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -NoProfile -ExecutionPolicy Bypass -Command \"" + full.Replace("\"", "\\\"") + "\"") { UseShellExecute = true });
        }

        // ---------------- Aplicar / Revertir ----------------
        public ApplyResult ApplyTweak(Tweak t)
        {
            var res = new ApplyResult { Id = t.Id, Ok = true, Reboot = t.Reboot };
            var backup = new List<OpBackup>();
            foreach (var op in t.Ops)
            {
                try
                {
                    switch (op.Type)
                    {
                        case "reg":
                        {
                            var kind = ParseKind(op.Kind);
                            var prior = RegGet(op.Path, op.Name);
                            backup.Add(new OpBackup { Type = "reg", Path = op.Path, Name = op.Name, PriorExists = prior.exists, PriorValue = prior.val, PriorKind = prior.kind });
                            RegSet(op.Path, op.Name, ConvertValue(op, kind), kind);
                            break;
                        }
                        case "service":
                        {
                            string svcPath = "HKLM:\\SYSTEM\\CurrentControlSet\\Services\\" + op.Name;
                            var prior = RegGet(svcPath, "Start");
                            backup.Add(new OpBackup { Type = "reg", Path = svcPath, Name = "Start", PriorExists = prior.exists, PriorValue = prior.val, PriorKind = prior.kind });
                            int start = StartMap.TryGetValue(op.Startup ?? "Manual", out var s) ? s : 3;
                            RegSet(svcPath, "Start", start, RegistryValueKind.DWord);
                            if (start == 4) RunPowerShellHidden("Stop-Service '" + op.Name + "' -Force -ErrorAction SilentlyContinue");
                            break;
                        }
                        case "command":
                            backup.Add(new OpBackup { Type = "command", Revert = op.Revert });
                            if (!string.IsNullOrEmpty(op.Apply)) RunPowerShellHidden(op.Apply);
                            break;
                        case "appx":
                            backup.Add(new OpBackup { Type = "appx" });
                            RunPowerShellHidden("Get-AppxPackage -Name '" + op.Package + "' -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue");
                            break;
                        default:
                            res.Errors.Add("op desconocida: " + op.Type); break;
                    }
                }
                catch (Exception ex) { res.Errors.Add(op.Type + ": " + ex.Message); res.Ok = false; }
            }
            File.WriteAllText(BackupPath(t.Id), JsonSerializer.Serialize(backup, _json));
            Log("APLICADO " + t.Id + (res.Errors.Count > 0 ? " | errores: " + string.Join("; ", res.Errors) : ""));
            return res;
        }

        public ApplyResult RevertTweak(string id)
        {
            var res = new ApplyResult { Id = id, Ok = true };
            var path = BackupPath(id);
            if (!File.Exists(path)) { res.Ok = false; res.Errors.Add("sin backup"); return res; }
            var ops = JsonSerializer.Deserialize<List<OpBackup>>(File.ReadAllText(path), _json);
            foreach (var b in ops)
            {
                try
                {
                    if (b.Type == "reg")
                    {
                        if (b.PriorExists) RegSet(b.Path, b.Name, ConvertStored(b.PriorValue, ParseKind(b.PriorKind)), ParseKind(b.PriorKind));
                        else RegDelete(b.Path, b.Name);
                    }
                    else if (b.Type == "command" && !string.IsNullOrEmpty(b.Revert)) RunPowerShellHidden(b.Revert);
                    else if (b.Type == "appx") res.Errors.Add("appx: reinstalar desde la Store (no auto-reversible)");
                }
                catch (Exception ex) { res.Errors.Add(b.Type + ": " + ex.Message); res.Ok = false; }
            }
            File.Delete(path);
            Log("REVERTIDO " + id);
            return res;
        }

        private static RegistryValueKind ParseKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return RegistryValueKind.DWord;
            switch (kind)
            {
                case "DWord": return RegistryValueKind.DWord;
                case "QWord": return RegistryValueKind.QWord;
                case "String": return RegistryValueKind.String;
                case "ExpandString": return RegistryValueKind.ExpandString;
                case "MultiString": return RegistryValueKind.MultiString;
                case "Binary": return RegistryValueKind.Binary;
                default: return RegistryValueKind.DWord;
            }
        }

        // ---------------- Codigo (compatible con la version PowerShell/web) ----------------
        public string ExportCode(IEnumerable<string> ids)
        {
            var payload = JsonSerializer.Serialize(new { v = 1, t = ids.ToArray() });
            return "TOJI-1-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }
        public List<string> ImportCode(string code)
        {
            var b64 = code.Trim();
            if (b64.StartsWith("TOJI-1-")) b64 = b64.Substring(7);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").EnumerateArray().Select(e => e.GetString()).ToList();
        }

        // ---------------- Utilidades ----------------
        public bool CreateRestorePoint()
        {
            try
            {
                RunPowerShellHidden("Set-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord -Force; Enable-ComputerRestore -Drive $env:SystemDrive\\; Checkpoint-Computer -Description 'TOJI' -RestorePointType MODIFY_SETTINGS");
                return true;
            }
            catch { return false; }
        }
        public void ClearTemp()
        {
            RunPowerShellHidden("Remove-Item \"$env:TEMP\\*\",\"$env:SystemRoot\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Clear-RecycleBin -Force -ErrorAction SilentlyContinue");
        }
    }
}
