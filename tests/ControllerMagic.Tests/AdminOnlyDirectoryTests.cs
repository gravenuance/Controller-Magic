using System.Security.AccessControl;
using System.Security.Principal;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class AdminOnlyDirectoryTests
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    [Fact]
    public void CreateSecurity_GrantsOnlyAdministratorsAndSystem_AndBlocksInheritance()
    {
        var security = AdminOnlyDirectory.CreateSecurity();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(Administrators, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
            Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
        });
        Assert.Equal(
            new[] { Administrators, LocalSystem }.Select(s => s.Value).Order(),
            rules.Select(r => r.IdentityReference.Value).Order());
    }

    [Fact]
    public void IsTrustedOwner_AcceptsAdministratorsSystemAndTheElevatedUserOnly()
    {
        var elevatedUser = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        var otherUser = new SecurityIdentifier("S-1-5-21-1-2-3-1002");

        Assert.True(AdminOnlyDirectory.IsTrustedOwner(Administrators, elevatedUser));
        Assert.True(AdminOnlyDirectory.IsTrustedOwner(LocalSystem, elevatedUser));
        Assert.True(AdminOnlyDirectory.IsTrustedOwner(elevatedUser, elevatedUser));
        Assert.False(AdminOnlyDirectory.IsTrustedOwner(otherUser, elevatedUser));
        Assert.False(AdminOnlyDirectory.IsTrustedOwner(Users, elevatedUser));
        Assert.False(AdminOnlyDirectory.IsTrustedOwner(otherUser, currentUser: null));
    }
}
