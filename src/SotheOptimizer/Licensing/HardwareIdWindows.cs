using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Sothe.Licensing
{
    // Lectura real de los componentes en Windows (WMI + registro)
    [SupportedOSPlatform("windows")]
    public static class HardwareIdWindows
    {
        public static string Current()
        {
            string uuid = Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
            string board = Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            string guid = null;
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                guid = key?.GetValue("MachineGuid") as string;
            }
            catch { }
            return HardwareId.Compute(uuid, board, guid);
        }

        private static string Wmi(string query, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject o in searcher.Get())
                    using (o)
                    {
                        var v = o[property]?.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
            }
            catch { }
            return null;
        }
    }
}
