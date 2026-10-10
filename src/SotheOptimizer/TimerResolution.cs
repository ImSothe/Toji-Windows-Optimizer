using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Sothe
{
    // Timer Resolution a 0.5 ms mediante NtSetTimerResolution (ntdll). El temporizador de plataforma es
    // global: mientras un proceso mantenga la peticion, el sistema corre a esa resolucion. Por eso el modo
    // "fijo" arranca un proceso residente ligero al iniciar sesion que la mantiene sin soltarla.
    //
    // Al terminar, el residente SUELTA su peticion de forma explicita (Release) en vez de depender de que Windows
    // la libere al morir el proceso: asi la resolucion vuelve a la del sistema aunque el cierre sea ordenado,
    // y no se gasta bateria ni CPU de mas. Windows solo aplica la mas fina de las peticiones activas.
    public static class TimerResolution
    {
        // Sentinelas que usan los tweaks JSON (timer_res_fixed). El motor los enruta a este codigo, no a PowerShell.
        public const string InstallSentinel = "Install-SotheTimerRes";
        public const string UninstallSentinel = "Uninstall-SotheTimerRes";

        public const string ResidentArg = "--timer-resident";
        public const string TaskName = "SotheOptimizerTimerRes";
        private const string MutexName = @"Global\SotheOptimizerTimerRes";
        public const string StopEventName = @"Global\SotheOptimizerTimerResStop";
        private const uint HalfMs100ns = 5000;   // 0.5 ms expresado en unidades de 100 ns

        // BOOLEAN de ntdll ocupa 1 byte (no los 4 del BOOL por defecto de .NET)
        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.U1)] bool setResolution, out uint currentResolution);
        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

        private static readonly object Gate = new object();
        private static uint _requested;   // valor que pidio ESTE proceso (0 = no hay peticion activa)

        // Fija la resolucion mas fina posible >= 0.5 ms en el proceso actual. Devuelve la resolucion real (100 ns).
        // Lanza si Windows rechaza la peticion (NTSTATUS distinto de 0), en vez de darla por buena.
        [SupportedOSPlatform("windows")]
        public static uint Apply()
        {
            lock (Gate)
            {
                uint desired = HalfMs100ns;
                if (NtQueryTimerResolution(out _, out uint maximum, out _) == 0 && maximum > 0)
                    desired = Math.Max(HalfMs100ns, maximum);   // 'maximum' es el valor mas fino que soporta el HW
                int status = NtSetTimerResolution(desired, true, out uint current);
                if (status != 0) throw new InvalidOperationException($"NtSetTimerResolution fallo (NTSTATUS 0x{status:X8}).");
                _requested = desired;
                return current;
            }
        }

        // Suelta la peticion de este proceso. Es seguro llamarla varias veces o sin haber llamado a Apply.
        [SupportedOSPlatform("windows")]
        public static void Release()
        {
            lock (Gate)
            {
                if (_requested == 0) return;
                NtSetTimerResolution(_requested, false, out _);   // set=false retira la peticion hecha con ese mismo valor
                _requested = 0;
            }
        }

        public static bool IsApplied { get { lock (Gate) return _requested != 0; } }

        public static double CurrentMs()
        {
            if (!OperatingSystem.IsWindows()) return 0;
            return NtQueryTimerResolution(out _, out _, out uint current) == 0 ? current / 10000.0 : 0;
        }

        // Proceso residente: fija 0.5 ms y se queda vivo (sin CPU) hasta que se le pida parar o termine la sesion;
        // entonces suelta la resolucion. Un mutex global evita que arranque dos veces.
        [SupportedOSPlatform("windows")]
        public static void RunResident()
        {
            using var single = new Mutex(true, MutexName, out bool created);
            if (!created) return;
            // La DACL por defecto de un evento creado por un proceso elevado solo deja senalarlo a administradores/SYSTEM
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
            EventHandler onExit = (_, __) => Release();
            AppDomain.CurrentDomain.ProcessExit += onExit;
            try
            {
                Apply();
                stop.WaitOne();   // bloquea (0 % CPU) hasta que el desinstalador senale el evento
            }
            finally
            {
                Release();
                AppDomain.CurrentDomain.ProcessExit -= onExit;
            }
        }

        // ---- Instalacion como tarea programada al iniciar sesion (elevada) ----
        // La tarea corre con RunLevel Highest: el .exe que ejecuta TIENE que estar en una carpeta que solo pueda
        // modificar un administrador (Archivos de programa). Si apuntara a Descargas, cualquier programa sin privilegios
        // podria sustituirlo y obtener administrador en el siguiente inicio de sesion.
        // Se usan los cmdlets ScheduledTasks para no pelear con el escapado de comillas de schtasks.
        public static string BuildInstallScript(string exePath)
        {
            string exe = (exePath ?? "").Replace("'", "''");
            return
                $"$a = New-ScheduledTaskAction -Execute '{exe}' -Argument '{ResidentArg}'; " +
                "$t = New-ScheduledTaskTrigger -AtLogOn; " +
                "$p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive; " +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
                $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; " +
                // Que otros procesos tambien vean la resolucion fina (efectivo tras reiniciar)
                "New-Item -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel' -Force -ErrorAction SilentlyContinue | Out-Null; " +
                "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel' -Name GlobalTimerResolutionRequests -Value 1 -Type DWord -Force; " +
                $"Start-ScheduledTask -TaskName '{TaskName}'";
        }

        // Pide al residente que pare por las buenas (suelta la resolucion), con la tarea aun registrada
        private static string StopResidentScript =>
            "try { $e = [System.Threading.EventWaitHandle]::OpenExisting('" + StopEventName + "'); [void]$e.Set(); Start-Sleep -Seconds 2 } catch { }; ";

        public static string BuildStopScript() =>
            StopResidentScript +
            $"Stop-ScheduledTask -TaskName '{TaskName}' -ErrorAction SilentlyContinue";

        public static string BuildUninstallScript() =>
            StopResidentScript +
            $"Stop-ScheduledTask -TaskName '{TaskName}' -ErrorAction SilentlyContinue; " +
            $"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue; " +
            "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel' -Name GlobalTimerResolutionRequests -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue; " +
            $"Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%{ResidentArg}%'\" -ErrorAction SilentlyContinue | " +
            // Este mismo PowerShell lleva el script (y por tanto el texto buscado) en su linea de comandos: sin excluirlo
            // se mataria a si mismo, el revertido constaria como fallido y el ajuste seguiria marcado como aplicado
            "Where-Object { $_.ProcessId -ne $PID } | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";

        // Copia el .exe en curso a la carpeta protegida (con ACL de solo administradores) y devuelve su ruta.
        // Antes se para el residente anterior: un .exe en uso no se puede sobrescribir.
        [SupportedOSPlatform("windows")]
        public static string PrepareProtectedCopy(string sourceExe)
        {
            if (string.IsNullOrEmpty(sourceExe) || !File.Exists(sourceExe))
                throw new FileNotFoundException("No se encuentra el ejecutable de Sothe Optimizer.", sourceExe);
            var dir = SystemPaths.ProtectedInstallDir;
            Directory.CreateDirectory(dir);
            SecureStorage.ApplyProtectedAcl(dir, inheritToChildren: true);
            var target = Path.Combine(dir, "SotheOptimizer.exe");
            if (!string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                PowerShellRunner.Run(BuildStopScript());
                File.Copy(sourceExe, target, true);
            }
            return target;
        }

        [SupportedOSPlatform("windows")]
        public static void InstallResident() => PowerShellRunner.Run(BuildInstallScript(PrepareProtectedCopy(Environment.ProcessPath)));

        [SupportedOSPlatform("windows")]
        public static void UninstallResident()
        {
            PowerShellRunner.Run(BuildUninstallScript());
            // La copia protegida ya no hace falta (best effort: si aun esta en uso se queda hasta la proxima vez)
            try { File.Delete(Path.Combine(SystemPaths.ProtectedInstallDir, "SotheOptimizer.exe")); } catch { }
        }
    }
}
