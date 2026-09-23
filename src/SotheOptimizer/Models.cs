using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sothe
{
    // Mapea tweaks.json (mismas claves, System.Text.Json con case-insensitive)
    public class Tweak
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Danger { get; set; }        // safe | moderate | high | extreme
        public bool IsPro { get; set; }           // true = necesita Sothe PRO para aplicarlo (sin la propiedad: gratis)
        public bool Portable { get; set; }
        public bool Anticheat { get; set; }
        public bool Reboot { get; set; }
        public string Desc { get; set; }
        public string Warning { get; set; }
        public List<TweakOp> Ops { get; set; } = new List<TweakOp>();
    }

    public class TweakOp
    {
        public string Type { get; set; }          // reg | service | command | appx
        public string Path { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }          // DWord (por defecto) | String | QWord ...
        public string Startup { get; set; }       // Disabled | Automatic | Manual
        public string Package { get; set; }
        public string Apply { get; set; }
        public string Revert { get; set; }
        public JsonElement Value { get; set; }     // numero o cadena; puede faltar
    }

    public class TweaksFile { public List<Tweak> Tweaks { get; set; } = new List<Tweak>(); }

    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Desc { get; set; }
        public List<string> Tweaks { get; set; } = new List<string>();
        [JsonIgnore] public bool IsCustom { get; set; }   // creado por el usuario (profiles.user.json)
    }
    public class ProfilesFile { public List<Profile> Profiles { get; set; } = new List<Profile>(); }

    // apps.json (pestana Apps / winget)
    public class AppItem
    {
        public string Id { get; set; }            // id de winget, p. ej. "Valve.Steam"
        public string Name { get; set; }
        public string Category { get; set; }
        public string Desc { get; set; }
    }
    public class AppsFile { public List<AppItem> Apps { get; set; } = new List<AppItem>(); }

    // features.json (pestana Caracteristicas de Windows)
    public class FeatureItem
    {
        public string Name { get; set; }
        public string Feature { get; set; }       // nombre DISM, p. ej. "Microsoft-Hyper-V-All"
        public string Desc { get; set; }
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
    }

    public class ApplyResult
    {
        public string Id { get; set; }
        public bool Ok { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool Reboot { get; set; }
    }
}
