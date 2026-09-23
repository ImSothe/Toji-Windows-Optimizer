using System.IO;
using System.Runtime.Versioning;

namespace Toji.Licensing
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
            if (LicenseConfig.IsConfigured)
                provider = new KeygenLicenseProvider(LicenseConfig.KeygenAccountId, LicenseConfig.TrialWorkerUrl);
            else
#if DEBUG
                provider = new DemoLicenseProvider(root);
#else
                provider = new UnconfiguredLicenseProvider();
#endif
            return new LicenseService(provider, new LicenseStore(Path.Combine(root, "license.dat"), new DpapiProtector()), fingerprint);
        }
    }
}
