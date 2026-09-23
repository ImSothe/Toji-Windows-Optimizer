using System;
using System.IO;
using System.Text.Json;

namespace Sothe.Licensing
{
    // Guarda la cache de licencia cifrada (DPAPI en Windows). Un fichero corrupto o de otro PC = sin licencia.
    public sealed class LicenseStore
    {
        private readonly string _path;
        private readonly ISecretProtector _protector;

        public LicenseStore(string path, ISecretProtector protector) { _path = path; _protector = protector; }

        public LicenseCache Load()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                return JsonSerializer.Deserialize<LicenseCache>(_protector.Unprotect(File.ReadAllBytes(_path)));
            }
            catch { return null; }
        }

        public void Save(LicenseCache cache)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(cache)));
            File.Move(tmp, _path, true);
        }
    }
}
