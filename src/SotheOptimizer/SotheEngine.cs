using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Sothe
{
    public class SotheEngine
    {
        public readonly string Root;                  // %ProgramData%\SotheOptimizer (backups, log, perfiles del usuario)
        public readonly string BackupDir;
        public readonly string BaseDir;               // carpeta del .exe (data\ con los JSON de la OTA)
        public readonly string DataPublicKey;         // clave con la que deben estar firmados los JSON de data\
        // camelCase al escribir: mismo formato que los JSON de data\ (y que las versiones PowerShell/web)
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public SotheEngine() : this(AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SotheOptimizer")) { }

        public SotheEngine(string baseDir, string root, string dataPublicKey = SignedData.PublicKey)
        {
            BaseDir = baseDir;
            Root = root;
            DataPublicKey = dataPublicKey;
            BackupDir = Path.Combine(Root, "backups");
            Directory.CreateDirectory(BackupDir);
            try { MigrateLegacyCustomProfiles(); } catch (Exception ex) { Log("Migracion de perfiles: " + ex.Message); }
        }

        public string DataDir => Path.Combine(BaseDir, "data");
        public string DataPath(string file) => Path.Combine(DataDir, file);

        // Texto de un JSON de datos. El de data\ (lo escribe la OTA) solo se usa si esta firmado con la clave del autor,
        // nadie lo ha tocado y es mas nuevo que la copia incrustada en el .exe. Si falta la firma, no cuadra o alguien lo
        // ha puesto o editado a mano, se ignora y manda la copia incrustada. null si no hay ninguno.
        public string ReadDataText(string file)
        {
            var bytes = SignedData.ReadVerifiedFile(DataDir, file, DataPublicKey, out int version, out string reason);
            if (bytes != null && version > EmbeddedDataVersion)
            {
                using var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return r.ReadToEnd();
            }
            if (reason != null && _rejected.Add(file)) Log($"data\\{file} ignorado ({reason}): se usa la copia incrustada en el .exe");
            return ReadEmbeddedText(file);
        }
        private readonly HashSet<string> _rejected = new HashSet<string>();

        // Version de los JSON incrustados: la del manifest.json que "OtaSigner sign" deja en src\SotheOptimizer\data (0 si no hay).
        // Una OTA solo sustituye a lo incrustado si es mas nueva, asi un .exe nuevo no se queda con datos viejos de disco
        public static int EmbeddedDataVersion => _embeddedVersion.Value;
        private static readonly Lazy<int> _embeddedVersion = new Lazy<int>(() =>
        {
            try
            {
                var text = ReadEmbeddedText(SignedData.ManifestFile);
                return text == null ? 0 : SignedData.ParseManifest(Encoding.UTF8.GetBytes(text))?.Version ?? 0;
            }
            catch (JsonException) { return 0; }
        });

        private static string ReadEmbeddedText(string file)
        {
            using var s = typeof(SotheEngine).Assembly.GetManifestResourceStream(EmbeddedDataPrefix + file);
            if (s == null) return null;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        public const string EmbeddedDataPrefix = "data/";

        public void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Root, "sothe.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); }
            catch { }
        }

        // ---------------- Carga de datos ----------------
        public List<Tweak> LoadTweaks()
        {
            var text = ReadDataText("tweaks.json") ?? throw new FileNotFoundException("No se encuentra tweaks.json", DataPath("tweaks.json"));
            return JsonSerializer.Deserialize<TweaksFile>(text, _json)?.Tweaks ?? new List<Tweak>();
        }
        public List<AppItem> LoadApps()
        {
            var text = ReadDataText("apps.json");
            if (text == null) return new List<AppItem>();
            return JsonSerializer.Deserialize<AppsFile>(text, _json)?.Apps ?? new List<AppItem>();
        }
        public List<FeatureItem> LoadFeatures()
        {
            var text = ReadDataText("features.json");
            if (text == null) return new List<FeatureItem>();
            return JsonSerializer.Deserialize<FeaturesFile>(text, _json)?.Features ?? new List<FeatureItem>();
        }

        // Perfiles oficiales (data\profiles.json, actualizables por OTA) + perfiles propios del usuario
        public List<Profile> LoadProfiles()
        {
            var official = ReadOfficialProfiles().Profiles;
            var ids = new HashSet<string>(official.Select(p => p.Id));
            var user = ReadProfilesFile(UserProfilesPath).Profiles.Where(p => !ids.Contains(p.Id)).ToList();
            foreach (var p in user) p.IsCustom = true;
            return official.Concat(user).ToList();
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
        // Lanza una excepcion si el comando falla: ApplyTweak/RevertTweak la registran como error de ese ajuste
        public void RunPowerShellHidden(string command) => PowerShellRunner.Run(command);

        // Ejecuta un comando de tweak. Los sentinelas de Timer Resolution se enrutan a codigo C# (ntdll +
        // tarea programada) en vez de a PowerShell; el resto son comandos normales.
        private void RunCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;
            if (command == TimerResolution.InstallSentinel) { if (OperatingSystem.IsWindows()) TimerResolution.InstallResident(); return; }
            if (command == TimerResolution.UninstallSentinel) { if (OperatingSystem.IsWindows()) TimerResolution.UninstallResident(); return; }
            RunPowerShellHidden(command);
        }
        public void RunPowerShellVisible(string title, string command)
        {
            // El titulo va entre comillas simples de PowerShell: solo letras, cifras y signos inofensivos
            title = SafeTitle(title);
            var full = "$host.UI.RawUI.WindowTitle='" + title + "'; Write-Host '=== " + title + " ===' -ForegroundColor Green; " + command + "; Write-Host ''; Write-Host 'Terminado. Puedes cerrar esta ventana.' -ForegroundColor Green";
            Process.Start(new ProcessStartInfo(SystemPaths.WindowsPowerShell, "-NoExit -NoProfile -ExecutionPolicy Bypass -Command \"" + full.Replace("\"", "\\\"") + "\"") { UseShellExecute = true });
        }

        public static string SafeTitle(string title)
        {
            var clean = new string((title ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '.' || c == '(' || c == ')').ToArray()).Trim();
            return clean.Length == 0 ? "Sothe" : clean;
        }

        // Nombres de servicio y de paquete Appx que acaban dentro de un comando de PowerShell. Vienen de tweaks.json (firmado),
        // pero se validan igualmente: una comilla, un punto y coma o un salto de linea romperian el comando.
        private static readonly System.Text.RegularExpressions.Regex ServiceNamePattern = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9_.@$-]{1,256}$");
        private static readonly System.Text.RegularExpressions.Regex AppxNamePattern = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9_.*-]{1,256}$");
        public static bool IsValidServiceName(string name) => name != null && ServiceNamePattern.IsMatch(name);
        public static bool IsValidAppxName(string name) => name != null && AppxNamePattern.IsMatch(name);

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
                            if (!IsValidServiceName(op.Name)) throw new ArgumentException("nombre de servicio no valido: " + op.Name);
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
                            RunCommand(op.Apply);
                            break;
                        case "appx":
                            if (!IsValidAppxName(op.Package)) throw new ArgumentException("nombre de paquete no valido: " + op.Package);
                            backup.Add(new OpBackup { Type = "appx" });
                            RunPowerShellHidden("Get-AppxPackage -Name '" + op.Package + "' -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue");
                            break;
                        default:
                            res.Errors.Add("op desconocida: " + op.Type); res.Ok = false; break;
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
                    else if (b.Type == "command" && !string.IsNullOrEmpty(b.Revert)) RunCommand(b.Revert);
                    else if (b.Type == "appx") res.Errors.Add("appx: reinstalar desde la Store (no auto-reversible)");
                }
                catch (Exception ex) { res.Errors.Add(b.Type + ": " + ex.Message); res.Ok = false; }
            }
            // Si algo fallo, el backup se conserva: el ajuste sigue contando como aplicado y se puede reintentar
            if (res.Ok) { File.Delete(path); Log("REVERTIDO " + id); }
            else Log("REVERTIDO CON ERRORES " + id + " (se conserva el backup) | " + string.Join("; ", res.Errors));
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

        // ---------------- Codigo para compartir una configuracion ----------------
        public const string ExportPrefix = "SOTHE-1-";
        public string ExportCode(IEnumerable<string> ids)
        {
            var payload = JsonSerializer.Serialize(new { v = 1, t = ids.ToArray() });
            return ExportPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }
        public List<string> ImportCode(string code)
        {
            var b64 = code.Trim();
            if (b64.StartsWith(ExportPrefix, StringComparison.OrdinalIgnoreCase)) b64 = b64.Substring(ExportPrefix.Length);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("t").EnumerateArray().Select(e => e.GetString()).ToList();
        }

        // ---------------- Utilidades ----------------
        public bool CreateRestorePoint()
        {
            try
            {
                RunPowerShellHidden("Set-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord -Force; Enable-ComputerRestore -Drive $env:SystemDrive\\; Checkpoint-Computer -Description 'Sothe Optimizer' -RestorePointType MODIFY_SETTINGS");
                return true;
            }
            catch { return false; }
        }
        public void ClearTemp()
        {
            RunPowerShellHidden("Remove-Item \"$env:TEMP\\*\",\"$env:SystemRoot\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue; Clear-RecycleBin -Force -ErrorAction SilentlyContinue");
        }

        // ---------------- Perfiles propios ----------------
        // Los perfiles del usuario viven fuera de data\ para que ni la OTA ni reinstalar la app los borren
        public const string CustomProfileDesc = "Perfil personalizado";
        private static readonly string[] BuiltInProfileKeys = { "competitivo", "ciber", "ciberseguridad", "limpio" };
        public string UserProfilesPath => Path.Combine(Root, "profiles.user.json");

        public static string ProfileId(string name)
        {
            string id = new string((name ?? "perfil").Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            return string.IsNullOrEmpty(id) ? "perfil" : id;
        }

        // Un nombre esta reservado si coincide (sin tildes ni mayusculas) con el id o el nombre de un perfil oficial
        public bool IsReservedProfileName(string name, out string suggestion)
        {
            var reserved = new HashSet<string>(BuiltInProfileKeys);
            foreach (var p in ReadOfficialProfiles().Profiles.Where(p => p.Desc != CustomProfileDesc))
            {
                reserved.Add(TextUtil.Fold(p.Id ?? ""));
                reserved.Add(TextUtil.Fold(ProfileId(p.Name)));
            }
            bool hit = reserved.Contains(TextUtil.Fold(ProfileId(name)));
            suggestion = hit ? (name ?? "").Trim() + "_Custom" : null;
            return hit;
        }

        public void SaveProfile(string name, List<string> ids)
        {
            name = (name ?? "").Trim();
            if (IsReservedProfileName(name, out var suggestion))
                throw new InvalidOperationException($"«{name}» es un perfil oficial de Sothe. Elige otro nombre, por ejemplo «{suggestion}».");
            string id = ProfileId(name);
            var doc = ReadProfilesFile(UserProfilesPath);
            doc.Profiles.RemoveAll(p => p.Id == id);
            doc.Profiles.Add(new Profile { Id = id, Name = name, Desc = CustomProfileDesc, Tweaks = ids });
            WriteJsonAtomic(UserProfilesPath, doc);
            Log("Perfil guardado: " + name);
        }

        // Versiones anteriores guardaban los perfiles propios dentro de data\profiles.json: se mueven al fichero de usuario
        public void MigrateLegacyCustomProfiles() => MoveCustomProfilesToUserFile(DataPath("profiles.json"));

        // Tambien la usa la OTA antes de sustituir profiles.json: asi la descarga fusiona y no pisa perfiles propios
        public int MoveCustomProfilesToUserFile(string officialPath)
        {
            if (!File.Exists(officialPath)) return 0;
            var doc = ReadProfilesFile(officialPath);
            var custom = doc.Profiles.Where(p => p.Desc == CustomProfileDesc).ToList();
            if (custom.Count == 0) return 0;
            var user = ReadProfilesFile(UserProfilesPath);
            foreach (var p in custom)
                if (!user.Profiles.Any(u => u.Id == p.Id)) user.Profiles.Add(p);
            WriteJsonAtomic(UserProfilesPath, user);
            doc.Profiles.RemoveAll(p => p.Desc == CustomProfileDesc);
            WriteJsonAtomic(officialPath, doc);
            Log($"Perfiles propios movidos a {UserProfilesPath}: {custom.Count}");
            return custom.Count;
        }

        private ProfilesFile ReadProfilesFile(string path) => ParseProfiles(File.Exists(path) ? File.ReadAllText(path) : null);
        private ProfilesFile ReadOfficialProfiles() => ParseProfiles(ReadDataText("profiles.json"));
        private ProfilesFile ParseProfiles(string text)
        {
            var doc = (text == null ? null : JsonSerializer.Deserialize<ProfilesFile>(text, _json)) ?? new ProfilesFile();
            doc.Profiles ??= new List<Profile>();
            return doc;
        }

        // Escritura atomica: si se corta a mitad no deja un JSON a medias
        internal void WriteJsonAtomic<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, _json));
            File.Move(tmp, path, true);
        }
    }
}
