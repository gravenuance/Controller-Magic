using System.Xml;
using System.Xml.Linq;

namespace ControllerMagic;

// The Task Scheduler definition behind "Start with Windows", and the check that a registered one
// still matches this exe. Pure XML work, kept apart from the schtasks.exe calls in StartupHelper.
internal static class StartupTaskDefinition
{
    public const string StartupArgument = "--startup";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // The logon trigger names the user: without one it means "any user", which needs admin to
    // register and would start the app for everyone who signs in.
    public static string BuildXml(string exePath, string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);

        var task = new XElement(Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "UserId", userSid))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "false"),
                new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(Ns + "Priority", "7")),
            new XElement(Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", exePath),
                    new XElement(Ns + "Arguments", StartupArgument))));

        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        new XDocument(new XDeclaration("1.0", "UTF-16", null), task).Save(writer);
        return writer.ToString();
    }

    // True when the registered task would not start this exe for this user: a different or
    // unreadable command, or a logon trigger for any user rather than a named one.
    public static bool NeedsRefresh(string registeredTaskXml, string exePath)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(registeredTaskXml).Root;
        }
        catch (XmlException)
        {
            return true;
        }

        string? command = root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command")?.Value;
        if (command is null || !IsSameExecutable(command, exePath))
            return true;

        var logonTriggers = root!.Element(Ns + "Triggers")?.Elements(Ns + "LogonTrigger").ToList() ?? [];
        return logonTriggers.Count == 0 ||
               logonTriggers.Exists(t => string.IsNullOrWhiteSpace(t.Element(Ns + "UserId")?.Value));
    }

    public static bool IsSameExecutable(string a, string b)
    {
        string? left = Normalize(a);
        string? right = Normalize(b);
        return left != null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string path)
    {
        string trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
            return null;

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
