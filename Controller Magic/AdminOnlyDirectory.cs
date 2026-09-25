using System.Security.AccessControl;
using System.Security.Principal;

namespace ControllerMagic;

// A directory only Administrators and SYSTEM can write to, so a file verified inside it stays the
// file that gets run. Must be called from an elevated process.
internal static class AdminOnlyDirectory
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    internal static DirectorySecurity CreateSecurity()
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { Administrators, LocalSystem })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        return security;
    }

    // A directory a standard user pre-created is theirs to tamper with whatever ACL we then set.
    internal static bool IsTrustedOwner(SecurityIdentifier owner, SecurityIdentifier? currentUser) =>
        owner == Administrators || owner == LocalSystem || owner == currentUser;

    // Creates each missing level with the admin-only ACL, then refuses any existing level that is a
    // link or owned by someone else, and resets its ACL to drop anything added since.
    public static bool TryEnsure(string path)
    {
        try
        {
            CreateSecurity().CreateDirectory(path);

            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                AppLog.Default.Warning($"AdminOnlyDirectory: {path} is a link; refusing to use it.");
                return false;
            }

            var owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            using var identity = WindowsIdentity.GetCurrent();
            if (owner is null || !IsTrustedOwner(owner, identity.User))
            {
                AppLog.Default.Warning($"AdminOnlyDirectory: {path} is owned by {owner?.Value ?? "nobody"}; refusing to use it.");
                return false;
            }

            info.SetAccessControl(CreateSecurity());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PrivilegeNotHeldException)
        {
            AppLog.Default.Warning($"AdminOnlyDirectory: could not secure {path}", ex);
            return false;
        }
    }
}
