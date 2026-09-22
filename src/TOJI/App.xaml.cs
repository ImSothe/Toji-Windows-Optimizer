using System;
using System.IO;
using System.Windows;

namespace Toji
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string flag = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TOJI", "eula.accepted");

            if (!File.Exists(flag))
            {
                var eula = new EulaWindow();
                bool ok = eula.ShowDialog() == true;
                if (!ok) { Shutdown(); return; }
                try { Directory.CreateDirectory(Path.GetDirectoryName(flag)); File.WriteAllText(flag, DateTime.Now.ToString("o")); } catch { }
            }

            new MainWindow().Show();
        }
    }
}
