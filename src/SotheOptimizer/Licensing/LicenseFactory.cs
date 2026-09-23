using System.IO;
using System.Runtime.Versioning;

namespace Sothe.Licensing
{
    [SupportedOSPlatform("windows")]
    public static class LicenseFactory
    {
        public static LicenseService CreateDefault(string root)
        {
            string fingerprint;
            try { fingerprint = HardwareIdWindows.Current(); }
            catch { fingerprint = "UNIDENTIFIED"; }

            ILicenseProvider provider;
#if DEBUG
            // Solo en Debug: SOTHE_LICENSE_DEMO=1 usa el proveedor DEMO aunque Keygen este configurado
            if (!LicenseConfig.IsConfigured || System.Environment.GetEnvironmentVariable("SOTHE_LICENSE_DEMO") == "1")
                provider = new DemoLicenseProvider(root);
            else
#endif
            if (LicenseConfig.IsConfigured)
                provider = new KeygenLicenseProvider(LicenseConfig.KeygenAccountId, LicenseConfig.KeygenProductId,
                    LicenseConfig.KeygenTrialPolicyId, LicenseConfig.TrialWorkerUrl);
            else
                provider = new UnconfiguredLicenseProvider();
            return new LicenseService(provider, new LicenseStore(Path.Combine(root, "license.dat"), new DpapiProtector()), fingerprint);
        }
    }
}
