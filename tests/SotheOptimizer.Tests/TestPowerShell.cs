using System.Diagnostics;

namespace Sothe.Tests
{
    // Elige PowerShell 7 (pwsh) si esta instalado y, si no, powershell.exe 5.1 de Windows
    public static class TestPowerShell
    {
        public static void Use()
        {
            foreach (var exe in new[] { "pwsh", "powershell.exe" })
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(exe, "-NoProfile -Command exit 0") { UseShellExecute = false, CreateNoWindow = true });
                    p.WaitForExit();
                    if (p.ExitCode == 0) { PowerShellRunner.Executable = exe; return; }
                }
                catch { }
            }
        }
    }
}
