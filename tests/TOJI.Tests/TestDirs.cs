using System;
using System.IO;

namespace Toji.Tests
{
    // Carpeta temporal con una copia de data\ (BaseDir) y otra para %ProgramData%\TOJI (Root)
    public sealed class TestDirs : IDisposable
    {
        public string Base { get; } = Path.Combine(Path.GetTempPath(), "toji-test-" + Guid.NewGuid().ToString("N"));
        public string BaseDir => Path.Combine(Base, "app");
        public string Root => Path.Combine(Base, "programdata");
        public string Data => Path.Combine(BaseDir, "data");

        public TestDirs()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Root);
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(Data, Path.GetFileName(f)));
        }

        public TojiEngine Engine() => new TojiEngine(BaseDir, Root);

        public void Dispose() { try { Directory.Delete(Base, true); } catch { } }
    }
}
