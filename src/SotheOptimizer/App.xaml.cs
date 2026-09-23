using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Sothe
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Modo residente de Timer Resolution: sin interfaz, solo fija 0.5 ms y mantiene el proceso vivo.
            if (e.Args.Contains(TimerResolution.ResidentArg))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var worker = new System.Threading.Thread(() => { if (OperatingSystem.IsWindows()) TimerResolution.RunResident(); })
                { IsBackground = false, Name = "TimerResolutionResident" };
                worker.SetApartmentState(System.Threading.ApartmentState.MTA);
                worker.Start();
                return;
            }

            string flag = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SotheOptimizer", "eula.accepted");

            // Sin esto, al cerrarse el EULA (unica ventana abierta) WPF da la app por terminada
            // y el primer arranque se cierra justo despues de aceptar.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!File.Exists(flag))
            {
                var eula = new EulaWindow();
                bool ok = eula.ShowDialog() == true;
                if (!ok) { Shutdown(); return; }
                try { Directory.CreateDirectory(Path.GetDirectoryName(flag)); File.WriteAllText(flag, DateTime.Now.ToString("o")); } catch { }
            }

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
    }
}
