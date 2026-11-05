using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Sothe
{
    // %ProgramData%\SotheOptimizer guarda cosas que la app, siempre elevada, LEE Y EJECUTA mas tarde: los backups
    // (su campo "revert" es un comando de PowerShell), la actualizacion preparada y el script que la instala.
    // Por defecto ProgramData deja a cualquier usuario crear ficheros y carpetas dentro (y ser dueno de lo que cree),
    // asi que un usuario sin privilegios podria plantar un backup o pre-crear la carpeta y escalar a administrador.
    // Aqui se exige que la carpeta y TODO lo que contiene lo haya creado un administrador o el sistema, y se deja una
    // ACL protegida: SYSTEM y Administradores control total, el resto de usuarios solo lectura.
    [SupportedOSPlatform("windows")]
    public static class SecureStorage
    {
        private static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier LocalSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        private static readonly SecurityIdentifier TrustedInstaller =
            new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

        // true si la ultima llamada a Harden dejo la carpeta de datos en estado de confianza
        public static bool RootTrusted { get; private set; }

        public static bool IsTrustedOwner(SecurityIdentifier owner, IEnumerable<SecurityIdentifier> extraTrusted = null) =>
            owner != null && (owner.Equals(Admins) || owner.Equals(LocalSystem) || owner.Equals(TrustedInstaller)
                              || (extraTrusted?.Contains(owner) ?? false));

        private static SecurityIdentifier OwnerOf(FileSystemInfo info) =>
            (info is DirectoryInfo d ? (FileSystemSecurity)d.GetAccessControl() : ((FileInfo)info).GetAccessControl())
                .GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        public static bool IsOwnedByTrusted(string path, IEnumerable<SecurityIdentifier> extraTrusted = null)
        {
            try
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                return info.Exists && IsTrustedOwner(OwnerOf(info), extraTrusted);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { return false; }
        }

        // Prepara la carpeta de datos. Devuelve false (y no confia en nada de ella) si no se pudo asegurar.
        public static bool Harden(string root, Action<string> log = null, IEnumerable<SecurityIdentifier> extraTrusted = null)
        {
            log ??= _ => { };
            RootTrusted = false;
            try
            {
                var trusted = extraTrusted?.ToList();
                if (Directory.Exists(root) && !IsOwnedByTrusted(root, trusted))
                {
                    // Alguien sin privilegios creo la carpeta antes que la app: se aparta entera y se empieza de cero
                    var aside = root.TrimEnd('\\', '/') + ".untrusted-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    Directory.Move(root, aside);
                    log($"The data folder was not owned by an administrator: moved aside to {aside}");
                }
                Directory.CreateDirectory(root);
                ApplyProtectedAcl(root, inheritToChildren: true);

                // Todo lo que ya hubiera dentro y no sea de un administrador se elimina (backups plantados, scripts...)
                foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).ToList())
                {
                    if (IsTrustedOwner(OwnerOf(entry), trusted)) { ResetInheritance(entry); continue; }
                    log($"Removed because it is not owned by an administrator: {entry.FullName}");
                    try { if (entry is DirectoryInfo di) di.Delete(true); else entry.Delete(); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { log($"Could not remove {entry.FullName}: {ex.Message}"); return false; }
                }
                RootTrusted = true;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is SystemException)
            {
                log("Could not secure the data folder: " + ex.Message);
                return false;
            }
        }

        // DACL protegida (no hereda de ProgramData): SYSTEM y Administradores control total; Usuarios, solo lectura
        public static void ApplyProtectedAcl(string dir, bool inheritToChildren)
        {
            var flags = inheritToChildren ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            sec.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, flags, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(dir).SetAccessControl(sec);
        }

        // Que los hijos hereden la ACL protegida de su carpeta (y no una ACL explicita antigua)
        private static void ResetInheritance(FileSystemInfo entry)
        {
            if (entry is DirectoryInfo d)
            {
                var sec = d.GetAccessControl();
                sec.SetAccessRuleProtection(false, false);
                d.SetAccessControl(sec);
            }
            else
            {
                var f = (FileInfo)entry;
                var sec = f.GetAccessControl();
                sec.SetAccessRuleProtection(false, false);
                f.SetAccessControl(sec);
            }
        }
    }
}
