using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sothe
{
    // Texto en el otro idioma ("es") de un elemento de los datos. Los campos principales de los JSON estan en ingles.
    public class LocText
    {
        public string Name { get; set; }
        public string Desc { get; set; }
        public string Warning { get; set; }
    }

    // Mapea tweaks.json (mismas claves, System.Text.Json con case-insensitive)
    public class Tweak
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Danger { get; set; }        // safe | moderate | high | extreme
        public bool Portable { get; set; }
        public bool Anticheat { get; set; }
        public bool Reboot { get; set; }
        public string Desc { get; set; }
        public string Warning { get; set; }
        public LocText Es { get; set; }
        public List<TweakOp> Ops { get; set; } = new List<TweakOp>();

        // Texto segun el idioma elegido (ingles por defecto; espanol si existe el bloque "es")
        [JsonIgnore] public string DisplayName => Loc.Pick(Name, Es?.Name);
        [JsonIgnore] public string DisplayDesc => Loc.Pick(Desc, Es?.Desc);
        [JsonIgnore] public string DisplayWarning => Loc.Pick(Warning, Es?.Warning);
    }

    public class TweakOp
    {
        public string Type { get; set; }          // reg | regsub (el mismo valor en cada subclave de Path) | service | command | appx
        public string Path { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }          // DWord (por defecto) | String | QWord ...
        public string Startup { get; set; }       // Disabled | Automatic | Manual
        public string Package { get; set; }
        public string Apply { get; set; }
        public string Revert { get; set; }
        // Solo "command": script de PowerShell que escribe en stdout el estado actual ANTES de aplicar. Se guarda en el backup
        // y llega a "apply" y a "revert" como la variable $captured, para restaurar lo que habia y no un valor fijo.
        public string Capture { get; set; }
        public JsonElement Value { get; set; }     // numero o cadena; puede faltar
    }

    public class TweaksFile { public List<Tweak> Tweaks { get; set; } = new List<Tweak>(); }

    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Desc { get; set; }
        public List<string> Tweaks { get; set; } = new List<string>();
        public LocText Es { get; set; }
        [JsonIgnore] public bool IsCustom { get; set; }   // creado por el usuario (profiles.user.json)

        [JsonIgnore] public string DisplayName => Loc.Pick(Name, Es?.Name);
        // Los perfiles propios guardan "Perfil personalizado" como marca interna: se muestra en el idioma elegido
        [JsonIgnore] public string DisplayDesc => Desc == SotheEngine.CustomProfileDesc ? Loc.T("profile.custom") : Loc.Pick(Desc, Es?.Desc);
    }
    public class ProfilesFile { public List<Profile> Profiles { get; set; } = new List<Profile>(); }

    // apps.json (pestana Apps / winget)
    public class AppItem
    {
        public string Id { get; set; }            // id de winget, p. ej. "Valve.Steam"
        public string Name { get; set; }
        public string Category { get; set; }
        public string Desc { get; set; }
        public LocText Es { get; set; }
        [JsonIgnore] public string DisplayDesc => Loc.Pick(Desc, Es?.Desc);
    }
    public class AppsFile { public List<AppItem> Apps { get; set; } = new List<AppItem>(); }

    // features.json (pestana Caracteristicas de Windows)
    public class FeatureItem
    {
        public string Name { get; set; }
        public string Feature { get; set; }       // nombre DISM, p. ej. "Microsoft-Hyper-V-All"
        public string Desc { get; set; }
        public LocText Es { get; set; }
        [JsonIgnore] public string DisplayName => Loc.Pick(Name, Es?.Name);
        [JsonIgnore] public string DisplayDesc => Loc.Pick(Desc, Es?.Desc);
    }
    public class FeaturesFile { public List<FeatureItem> Features { get; set; } = new List<FeatureItem>(); }

    // Backup por-tweak para poder revertir al estado exacto anterior
    public class OpBackup
    {
        public string Type { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public bool PriorExists { get; set; }
        public string PriorValue { get; set; }
        public string PriorKind { get; set; }
        public string Revert { get; set; }         // para ops de tipo command
        public string Captured { get; set; }       // estado previo que devolvio "capture" (null si el ajuste no lo usa)
        public string Service { get; set; }        // servicio al que pertenece este valor Start (para volver a arrancarlo)
        public bool WasRunning { get; set; }       // el servicio estaba en ejecucion antes de aplicar
    }

    // Fichero de backup de un ajuste: secuencia de aplicacion (para revertir en orden LIFO) y estado previo de cada operacion
    public class BackupFile
    {
        public long Seq { get; set; }
        public List<OpBackup> Ops { get; set; } = new List<OpBackup>();
    }

    public class ApplyResult
    {
        public string Id { get; set; }
        public bool Ok { get; set; }
        public bool Skipped { get; set; }          // ya estaba aplicado: no se ha tocado nada
        public List<string> Errors { get; set; } = new List<string>();
        public bool Reboot { get; set; }
    }
}
