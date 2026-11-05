using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public const string ResolutionArg = "--resolution";   // en unidades de 100 ns; la calibracion decide cual
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
        public static uint Apply() => Apply(HalfMs100ns);

        // Igual, pidiendo "desired" (100 ns); nunca mas fina de lo que soporta el hardware
        [SupportedOSPlatform("windows")]
        public static uint Apply(uint desired)
        {
            lock (Gate)
            {
                if (NtQueryTimerResolution(out _, out uint maximum, out _) == 0 && maximum > 0)
                    desired = Math.Max(desired, maximum);   // 'maximum' es el valor mas fino que soporta el HW
                if (_requested != 0 && _requested != desired) NtSetTimerResolution(_requested, false, out _);
                OptOutOfTimerThrottling();
                int status = NtSetTimerResolution(desired, true, out uint current);
                if (status != 0) throw new InvalidOperationException($"NtSetTimerResolution failed (NTSTATUS 0x{status:X8}).");
                _requested = desired;
                return current;
            }
        }

        // Windows 11 puede ignorar la peticion de un proceso que no tiene ventana visible. Este indicador documentado
        // (PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION) pide que no lo haga. Best effort: si no existe, no hace nada.
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottlingState { public uint Version; public uint ControlMask; public uint StateMask; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

        [SupportedOSPlatform("windows")]
        public static bool OptOutOfTimerThrottling()
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = 0x4, StateMask = 0 };
            try { return SetProcessInformation(Process.GetCurrentProcess().Handle, 4, ref state, Marshal.SizeOf<PowerThrottlingState>()); }
            catch (EntryPointNotFoundException) { return false; }
        }

        // ---- Calibracion ----
        // Pedir exactamente 0.5 ms no siempre es lo que deja Sleep(1) mas cerca de 1 ms. En el PC de desarrollo,
        // 0.500 ms daba una media de 1.54 ms y 0.515 ms daba 1.16 ms. Depende del hardware: se mide, no se adivina.
        public readonly struct Probe
        {
            public Probe(uint resolution, double meanMs, double stdDevMs) { Resolution = resolution; MeanMs = meanMs; StdDevMs = stdDevMs; }
            public uint Resolution { get; }
            public double MeanMs { get; }
            public double StdDevMs { get; }
            public double Score => TimerResolution.Score(MeanMs, StdDevMs);
        }

        // Distancia al ideal Sleep(1) = 1 ms; la variabilidad (jitter) penaliza. Menor es mejor.
        public static double Score(double meanMs, double stdDevMs) => Math.Abs(meanMs - 1.0) + 0.5 * stdDevMs;

        // Gana la menor puntuacion; en empate, la resolucion mas fina. Funcion pura (se prueba sin Windows)
        public static Probe ChooseBest(IReadOnlyList<Probe> probes)
        {
            if (probes == null || probes.Count == 0) throw new ArgumentException("No candidates.", nameof(probes));
            var best = probes[0];
            foreach (var p in probes)
                if (p.Score < best.Score || (p.Score == best.Score && p.Resolution < best.Resolution)) best = p;
            return best;
        }

        // Barre "steps" candidatos desde la resolucion mas fina en pasos de "stepMs" y mide Sleep(1) en cada uno
        // (~0.5 s por candidato). Deja libre la peticion al terminar; quien llama decide si mantiene la ganadora.
        [SupportedOSPlatform("windows")]
        public static Probe Calibrate(int samples = 25, int steps = 11, double stepMs = 0.01)
        {
            if (samples < 3) throw new ArgumentOutOfRangeException(nameof(samples));
            if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps));
            if (stepMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepMs));
            if (NtQueryTimerResolution(out uint coarsest, out uint finest, out _) != 0 || finest == 0)
                throw new InvalidOperationException("Could not query the timer resolution.");
            bool wasHolding; uint previous;
            lock (Gate) { wasHolding = _requested != 0; previous = _requested; }
            if (wasHolding) Release();
            try
            {
                var probes = new List<Probe>(steps);
                for (int i = 0; i < steps; i++)
                {
                    uint candidate = finest + (uint)Math.Round(i * stepMs * 10_000);
                    if (candidate > coarsest) break;
                    Apply(candidate);
                    System.Threading.Thread.Sleep(30);   // deja que asiente el nuevo ritmo del temporizador
                    double mean = MeanSleepMs(samples, out double sd);
                    probes.Add(new Probe(candidate, mean, sd));
                    Release();
                }
                return ChooseBest(probes);
            }
            finally
            {
                Release();
                if (wasHolding) Apply(previous);
            }
        }

        private static double MeanSleepMs(int samples, out double stdDev)
        {
            var values = new List<double>(samples);
            var sw = new Stopwatch();
            for (int i = 0; i < samples + 1; i++)
            {
                sw.Restart();
                System.Threading.Thread.Sleep(1);
                if (i > 0) values.Add(sw.Elapsed.TotalMilliseconds);   // el primer sueno cae a mitad de tick: se descarta
                System.Threading.Thread.Sleep(20);
            }
            double mean = values.Average();
            stdDev = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
            return mean;
        }

        // Valor de --resolution de la linea de comandos; si falta o es absurdo, 0.5 ms
        public static uint ParseResolution(IReadOnlyList<string> args)
        {
            for (int i = 0; i + 1 < (args?.Count ?? 0); i++)
                if (args[i] == ResolutionArg && uint.TryParse(args[i + 1], out var v) && v >= HalfMs100ns && v <= 156_250) return v;
            return HalfMs100ns;
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
        public static void RunResident(uint resolution = HalfMs100ns)
        {
            using var single = new Mutex(true, MutexName, out bool created);
            if (!created) return;
            // La DACL por defecto de un evento creado por un proceso elevado solo deja senalarlo a administradores/SYSTEM
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
            EventHandler onExit = (_, __) => Release();
            AppDomain.CurrentDomain.ProcessExit += onExit;
            try
            {
                Apply(resolution);
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
        public static string BuildInstallScript(string exePath, uint resolution = HalfMs100ns)
        {
            string exe = (exePath ?? "").Replace("'", "''");
            return
                $"$a = New-ScheduledTaskAction -Execute '{exe}' -Argument '{ResidentArg} {ResolutionArg} {resolution}'; " +
                "$t = New-ScheduledTaskTrigger -AtLogOn; " +
                "$p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive; " +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
                $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; " +
                // GlobalTimerResolutionRequests ya no se toca aqui: es una operacion "reg" del ajuste, con su valor previo
                // guardado y su orden de reversion (antes el desinstalador lo ponia a 0 aunque otro ajuste lo hubiera activado)
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
                throw new FileNotFoundException("The Toji executable was not found.", sourceExe);
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
        public static void InstallResident()
        {
            // Se calibra una vez al instalar (unos 6 s, el PC en reposo) y la tarea arranca siempre con esa resolucion
            uint resolution = HalfMs100ns;
            try { resolution = Calibrate().Resolution; } catch (Exception ex) when (ex is InvalidOperationException) { }
            PowerShellRunner.Run(BuildInstallScript(PrepareProtectedCopy(Environment.ProcessPath), resolution));
        }

        [SupportedOSPlatform("windows")]
        public static void UninstallResident()
        {
            PowerShellRunner.Run(BuildUninstallScript());
            // La copia protegida ya no hace falta (best effort: si aun esta en uso se queda hasta la proxima vez)
            try { File.Delete(Path.Combine(SystemPaths.ProtectedInstallDir, "SotheOptimizer.exe")); } catch { }
        }
    }
}
