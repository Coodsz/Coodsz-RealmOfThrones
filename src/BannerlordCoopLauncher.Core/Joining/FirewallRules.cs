using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace BannerlordCoopLauncher.Core.Joining;

/// <summary>One Windows Firewall rule, as far as joining needs it.</summary>
/// <remarks>LocalUsers is the rule's LocalUserAuthorizedList (an SDDL like O:LSD:(A;;CC;;;S-1-5-21-...)); a rule with one
/// only covers those Windows accounts.</remarks>
public sealed record FirewallRuleInfo(string Name, bool Inbound, bool Allow, bool Enabled, int Protocol, string LocalPorts, string? Application, string? Service = null, string? LocalUsers = null);

public enum FirewallState
{
    /// <summary>An enabled inbound allow rule covers the port (or the server program).</summary>
    Allowed,
    /// <summary>An enabled inbound block rule hits the port or the server program. Block rules win.</summary>
    Blocked,
    /// <summary>No rule covers it. Windows may still ask the first time the server starts.</summary>
    Missing,
    /// <summary>The rules could not be read.</summary>
    Unknown
}

public enum FirewallAddOutcome
{
    Added,
    Declined,
    Failed
}

/// <summary>
/// Checks for and adds the inbound Windows Firewall rule the Coop server needs. Reading the rules
/// needs no admin rights (HNetCfg.FwPolicy2, the same list wf.msc shows, in every Windows language).
/// Adding goes through netsh in an elevated window, so Windows asks the player first.
/// </summary>
public static class FirewallRules
{
    public const int Udp = 17;
    public const int AnyProtocol = 256;
    public const string RuleName = "Coodsz Coop server";

    public static string PortRuleName(int port) => RuleName + " UDP " + port.ToString(CultureInfo.InvariantCulture);

    public static string ProgramRuleName => RuleName + " program";

    /// <summary>True when a rule's LocalPorts text ("*", "4200", "4000-4300", "80,4200") includes the port.</summary>
    public static bool PortMatches(string? localPorts, int port)
    {
        if (string.IsNullOrWhiteSpace(localPorts) || localPorts.Trim() == "*" || localPorts.Trim().Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var raw in localPorts.Split(','))
        {
            var part = raw.Trim();
            var dash = part.IndexOf('-');
            if (dash > 0
                && int.TryParse(part[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var low)
                && int.TryParse(part[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var high))
            {
                if (port >= low && port <= high)
                    return true;
            }
            else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single) && single == port)
                return true;
        }

        return false;
    }

    private static bool SameProgram(string? rule, string? program)
    {
        if (string.IsNullOrWhiteSpace(rule) || string.IsNullOrWhiteSpace(program))
            return false;
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(rule.Trim()))
                .Equals(Path.GetFullPath(program.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Decides whether friends' UDP packets on this port reach the server program.</summary>
    public static FirewallState Check(IEnumerable<FirewallRuleInfo>? rules, int port, string? program, string? currentUserSid = null)
    {
        if (rules == null)
            return FirewallState.Unknown;
        currentUserSid ??= CurrentUserSid();
        // A rule tied to a Windows service only covers that service, never the server. A rule
        // limited to other Windows accounts (such as the codex_sandbox_* rules, scoped to a sandbox
        // user) does not touch the server either, which runs as the current user.
        var relevant = rules.Where(rule => rule.Inbound && rule.Enabled && rule.Protocol is Udp or AnyProtocol && string.IsNullOrWhiteSpace(rule.Service)
            && CoversUser(rule.LocalUsers, currentUserSid)).ToList();
        bool Hits(FirewallRuleInfo rule)
        {
            var anyProgram = string.IsNullOrWhiteSpace(rule.Application);
            if (!anyProgram && !SameProgram(rule.Application, program))
                return false;
            return PortMatches(rule.LocalPorts, port);
        }

        if (relevant.Any(rule => !rule.Allow && Hits(rule)))
            return FirewallState.Blocked;
        return relevant.Any(rule => rule.Allow && Hits(rule)) ? FirewallState.Allowed : FirewallState.Missing;
    }

    // Groups every signed in user is in: Everyone, Authenticated Users, Users, Interactive.
    private static readonly HashSet<string> EveryUser = new(StringComparer.OrdinalIgnoreCase)
    {
        "WD", "AU", "BU", "IU", "S-1-1-0", "S-1-5-11", "S-1-5-32-545", "S-1-5-4"
    };

    /// <summary>True when a rule's LocalUserAuthorizedList (SDDL) is empty or names this user or a
    /// group every user is in. A list that names only other accounts means the rule is not for us.</summary>
    public static bool CoversUser(string? localUsers, string? currentUserSid)
    {
        if (string.IsNullOrWhiteSpace(localUsers))
            return true;
        var sids = System.Text.RegularExpressions.Regex.Matches(localUsers, @"\(A;[^;]*;[^;]*;[^;]*;[^;]*;(?<sid>[^)]+)\)")
            .Select(match => match.Groups["sid"].Value.Trim())
            .ToList();
        if (sids.Count == 0 || sids.Any(EveryUser.Contains))
            return true;
        // Without the current user's SID the rule cannot be ruled out.
        return string.IsNullOrWhiteSpace(currentUserSid) || sids.Contains(currentUserSid, StringComparer.OrdinalIgnoreCase);
    }

    public static string? CurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    public static string Describe(FirewallState state, int port) => state switch
    {
        FirewallState.Allowed => "Windows Firewall lets friends in on UDP " + port + ".",
        FirewallState.Blocked => "Windows Firewall has a rule that blocks the server. Open Windows Defender Firewall, Allow an app, and allow dotnet for private and public networks, or remove the block rule.",
        FirewallState.Missing => "No Windows Firewall rule for UDP " + port + " yet. Click Allow through firewall.",
        _ => "Could not read the Windows Firewall rules. Another firewall program may be in charge."
    };

    /// <summary>
    /// The netsh commands (one per line) that add the rules: one for the UDP port, one for the
    /// server program when its path is safe to pass on a command line.
    /// </summary>
    public static IReadOnlyList<string> AddCommands(int port, string? program)
    {
        var portText = port.ToString(CultureInfo.InvariantCulture);
        var commands = new List<string>
        {
            string.Join(" ", "netsh advfirewall firewall add rule", "name=\"" + PortRuleName(port) + "\"",
                "dir=in action=allow protocol=UDP", "localport=" + portText, "profile=any enable=yes")
        };
        if (ProgramIsSafe(program))
            commands.Add(string.Join(" ", "netsh advfirewall firewall add rule", "name=\"" + ProgramRuleName + "\"",
                "dir=in action=allow", "program=\"" + program + "\"", "protocol=UDP profile=any enable=yes"));
        return commands;
    }

    /// <summary>A path cmd.exe passes on unchanged inside quotes (no quote, percent or caret).</summary>
    public static bool ProgramIsSafe(string? program) =>
        !string.IsNullOrWhiteSpace(program)
        && Path.IsPathFullyQualified(program)
        && program.IndexOfAny(new[] { '"', '%', '^', '\r', '\n' }) < 0;

    /// <summary>cmd.exe /c with the commands joined by &amp;&amp;, run elevated so Windows asks first.</summary>
    public static ProcessStartInfo ElevatedStart(int port, string? program) => new("cmd.exe", "/c " + string.Join(" && ", AddCommands(port, program)))
    {
        UseShellExecute = true,
        Verb = "runas",
        WindowStyle = ProcessWindowStyle.Hidden
    };

    /// <summary>Asks Windows (UAC) to add the rules and waits for netsh. Never elevates without the prompt.</summary>
    public static FirewallAddOutcome Add(int port, string? program)
    {
        try
        {
            using var process = Process.Start(ElevatedStart(port, program));
            if (process == null)
                return FirewallAddOutcome.Failed;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0 ? FirewallAddOutcome.Added : FirewallAddOutcome.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return FirewallAddOutcome.Declined;
        }
        catch (Win32Exception)
        {
            return FirewallAddOutcome.Failed;
        }
    }

    public static string Describe(FirewallAddOutcome outcome) => outcome switch
    {
        FirewallAddOutcome.Added => "Added the Windows Firewall rule.",
        FirewallAddOutcome.Declined => "Windows asked and the answer was no, so nothing changed.",
        _ => "Could not add the Windows Firewall rule. Add an inbound rule for UDP by hand in Windows Defender Firewall."
    };

    /// <summary>The inbound rules from Windows, or null when they cannot be read.</summary>
    public static IReadOnlyList<FirewallRuleInfo>? Read()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null)
                return null;
            var policy = Activator.CreateInstance(type);
            var rules = GetValue(policy, "Rules");
            if (rules is not System.Collections.IEnumerable list)
                return null;
            var found = new List<FirewallRuleInfo>();
            foreach (var rule in list)
            {
                try
                {
                    // NET_FW_RULE_DIR_IN = 1, NET_FW_ACTION_ALLOW = 1.
                    var direction = Convert.ToInt32(GetValue(rule, "Direction"), CultureInfo.InvariantCulture);
                    if (direction != 1)
                        continue;
                    found.Add(new FirewallRuleInfo(
                        GetValue(rule, "Name") as string ?? "",
                        true,
                        Convert.ToInt32(GetValue(rule, "Action"), CultureInfo.InvariantCulture) == 1,
                        GetValue(rule, "Enabled") is true,
                        Convert.ToInt32(GetValue(rule, "Protocol"), CultureInfo.InvariantCulture),
                        GetValue(rule, "LocalPorts") as string ?? "*",
                        GetValue(rule, "ApplicationName") as string,
                        GetValue(rule, "ServiceName") as string,
                        LocalUsersOf(rule)));
                }
                catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException or FormatException or System.Runtime.InteropServices.COMException)
                {
                }
            }

            return found;
        }
        catch (Exception ex) when (ex is TargetInvocationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or MissingMethodException or NotSupportedException)
        {
            return null;
        }
    }

    // INetFwRule3.LocalUserAuthorizedList (Windows 8 and later); older rules have none.
    private static string? LocalUsersOf(object? rule)
    {
        try
        {
            return GetValue(rule, "LocalUserAuthorizedList") as string;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static object? GetValue(object? target, string property) =>
        target?.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);
}
