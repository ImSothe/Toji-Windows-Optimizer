using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Sothe
{
    // Idioma de la interfaz. Ingles por defecto; "es" cambia todos los textos de la app y de los datos (tweaks, apps,
    // caracteristicas, perfiles). Los textos viven en Strings.cs (clave -> ingles / espanol) y los de los JSON de datos
    // en el bloque "es" de cada elemento. Los mensajes tecnicos (registro de eventos, errores del motor) van siempre en
    // ingles. El idioma elegido se guarda en settings.json junto a los datos de la app.
    public sealed class Loc : INotifyPropertyChanged
    {
        public const string English = "en";
        public const string Spanish = "es";
        private const string SettingsFile = "settings.json";

        public static Loc Instance { get; } = new Loc();
        public static string Language { get; private set; } = English;
        public static bool IsSpanish => Language == Spanish;

        public event PropertyChangedEventHandler PropertyChanged;
        public static event Action LanguageChanged;

        // Indexador para los enlaces de XAML: {Binding [clave], Source={x:Static local:Loc.Instance}}
        public string this[string key] => T(key);

        public static string Normalize(string lang) =>
            string.Equals(lang, Spanish, StringComparison.OrdinalIgnoreCase) ? Spanish : English;

        public static void SetLanguage(string lang)
        {
            var l = Normalize(lang);
            if (l == Language) return;
            Language = l;
            CultureInfo.CurrentUICulture = new CultureInfo(l);
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }

        public static string T(string key)
        {
            var table = Language == Spanish ? Strings.Es : Strings.En;
            if (table.TryGetValue(key, out var s)) return s;
            return Strings.En.TryGetValue(key, out var en) ? en : key;   // sin traduccion: ingles; sin clave: la propia clave
        }

        public static string T(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

        // Para los datos: el texto en ingles, o el del bloque "es" si el idioma es espanol y existe
        public static string Pick(string english, string spanish) =>
            Language == Spanish && !string.IsNullOrEmpty(spanish) ? spanish : english;

        // ---- Persistencia ----
        public static string SettingsPath(string root) => Path.Combine(root, SettingsFile);

        public static void Load(string root)
        {
            try
            {
                var path = SettingsPath(root);
                if (!File.Exists(path)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("language", out var v) && v.ValueKind == JsonValueKind.String)
                    SetLanguage(v.GetString());
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) { }
        }

        public static void Save(string root)
        {
            try
            {
                Directory.CreateDirectory(root);
                var path = SettingsPath(root);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new { language = Language }));
                File.Move(tmp, path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
