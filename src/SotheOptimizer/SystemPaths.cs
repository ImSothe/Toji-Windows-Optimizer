using System;
using System.IO;

namespace Sothe
{
    // Rutas ABSOLUTAS de los programas del sistema que lanza la app (siempre elevada). Con un nombre suelto
    // ("cmd.exe", "powershell.exe") Windows busca primero en la carpeta del propio .exe: un cmd.exe o powershell.exe
    // dejado junto a SotheOptimizer.exe (por ejemplo en Descargas) se ejecutaria como administrador.
    public static class SystemPaths
    {
        public static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

        public static string WindowsPowerShell
        {
            get
            {
                var p = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                return File.Exists(p) ? p : "powershell.exe";   // fuera de Windows (tests en Linux) se queda el nombre
            }
        }

        // winget vive como alias de ejecucion en el perfil del usuario; si no esta, se cae al nombre (PATH)
        public static string Winget
        {
            get
            {
                var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "winget.exe");
                return File.Exists(alias) ? alias : "winget";
            }
        }

        // Carpeta de instalacion protegida (solo escribe un administrador) para el proceso residente de Timer Resolution
        public static string ProtectedInstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SotheOptimizer");
    }
}
