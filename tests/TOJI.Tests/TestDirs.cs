using System;
using System.IO;
using System.Security.Cryptography;

namespace Toji.Tests
{
    // Carpeta temporal con una copia de data\ (BaseDir) y otra para %ProgramData%\TOJI (Root).
    // Cada una tiene su par de claves de firma: el motor confia en su clave publica y Sign() firma como OtaSigner.
    public sealed class TestDirs : IDisposable
    {
        public string Base { get; } = Path.Combine(Path.GetTempPath(), "toji-test-" + Guid.NewGuid().ToString("N"));
        public string BaseDir => Path.Combine(Base, "app");
        public string Root => Path.Combine(Base, "programdata");
        public string Data => Path.Combine(BaseDir, "data");
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string PublicKey => ManifestSigner.PublicKeyBase64(Key);

        public TestDirs()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Root);
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(Data, Path.GetFileName(f)));
        }

        public TojiEngine Engine() => new TojiEngine(BaseDir, Root, PublicKey);

        // Firma data\ con la clave de esta carpeta (lo mismo que "OtaSigner sign")
        public void Sign(int version = 1) => ManifestSigner.SignDirectory(Data, Key, version);

        public void Dispose()
        {
            Key.Dispose();
            try { Directory.Delete(Base, true); } catch { }
        }
    }
}
