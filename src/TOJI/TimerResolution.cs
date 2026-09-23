using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Toji
{
    // Timer Resolution a 0.5 ms mediante NtSetTimerResolution (ntdll). El temporizador de plataforma es
    // global: mientras un proceso mantenga la peticion, el sistema corre a esa resolucion. Por eso el modo
    // "fijo" arranca un proceso residente ligero al iniciar sesion que la mantiene sin soltarla.
    public static class TimerResolution
    {
        // Sentinelas que usan los tweaks JSON (timer_res_fixed). El motor los enruta a este codigo, no a PowerShell.
        public const string InstallSentinel = "Install-TojiTimerRes";
        public const string UninstallSentinel = "Uninstall-TojiTimerRes";

        public const string ResidentArg = "--timer-resident";
        public const string TaskName = "SotheCelestialTimerRes";
        private const string MutexName = @"Global\SotheCelestialTimerRes";
        private const uint HalfMs100ns = 5000;   // 0.5 ms expresado en unidades de 100 ns

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

        // Fija la resolucion mas fina posible >= 0.5 ms en el proceso actual. Devuelve la resolucion real (100 ns).
        [SupportedOSPlatform("windows")]
        public static uint Apply()
        {
            uint desired = HalfMs100ns;
            if (NtQueryTimerResolution(out _, out uint maximum, out _) == 0 && maximum > 0)
                desired = Math.Max(HalfMs100ns, maximum);   // 'maximum' es el valor mas fino que soporta el HW
            NtSetTimerResolution(desired, true, out uint current);
            return current;
        }

        public static double CurrentMs()
        {
            if (!OperatingSystem.IsWindows()) return 0;
            return NtQueryTimerResolution(out _, out _, out uint current) == 0 ? current / 10000.0 : 0;
        }

        // Proceso residente: fija 0.5 ms y se queda vivo (sin CPU) para que la resolucion no se libere.
        // Un mutex global evita que arranque dos veces.
        [SupportedOSPlatform("windows")]
        public static void RunResident()
        {
            using var single = new Mutex(true, MutexName, out bool created);
            if (!created) return;
            Apply();
            using var forever = new ManualResetEventSlim(false);
            forever.Wait();   // bloquea indefinidamente; al morir el proceso el sistema libera la resolucion
        }

        // ---- Instalacion como tarea programada al iniciar sesion (elevada) ----
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

        public static string BuildUninstallScript() =>
            $"Stop-ScheduledTask -TaskName '{TaskName}' -ErrorAction SilentlyContinue; " +
            $"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue; " +
            "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel' -Name GlobalTimerResolutionRequests -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue; " +
            $"Get-CimInstance Win32_Process -Filter \"CommandLine LIKE '%{ResidentArg}%'\" -ErrorAction SilentlyContinue | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";

        [SupportedOSPlatform("windows")]
        public static void InstallResident() => PowerShellRunner.Run(BuildInstallScript(Environment.ProcessPath));

        [SupportedOSPlatform("windows")]
        public static void UninstallResident() => PowerShellRunner.Run(BuildUninstallScript());
    }
}
