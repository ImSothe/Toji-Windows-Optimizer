using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Toji.Licensing
{
    // Cifrado ligado a esta maquina: copiar license.dat a otro PC no sirve
    [SupportedOSPlatform("windows")]
    public sealed class DpapiProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TOJI-Celestial-license-v1");
        public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);
        public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine);
    }
}
