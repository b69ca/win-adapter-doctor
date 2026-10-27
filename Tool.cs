using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
namespace WinDiagnostics;

public static class Tool
{
    public const string Name = "win-adapter-doctor";
    public static readonly string[] ValueOptions = ["--adapter"];
    public static readonly string[] Flags = [];
    public const string Help = "win-adapter-doctor [--adapter NAME-SUBSTRING] [--json]\nInspect local adapter state, unicast addresses, gateways, configured DNS servers, MTU, and lifetime counters. No probes, packet capture, route changes, or repairs. Filter is a literal case-insensitive substring of the interface name.";
    public static void ValidateOptions(Options o)
    {
        if (o.Has("--adapter") && (string.IsNullOrWhiteSpace(o.Get("--adapter")) || o.Get("--adapter").Length > 256))
            throw new ArgumentException("--adapter must contain 1–256 nonblank characters.");
    }
    public static bool IsUsable(string text)
    {
        if (!IPAddress.TryParse(text, out var ip) || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.GetAddressBytes()[0] is not (0 or 127) && ip.GetAddressBytes()[0] < 224 && !IsApipa(text) && !ip.Equals(IPAddress.Broadcast);
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal && !ip.IsIPv6Multicast;
    }
    public static bool IsApipa(string text) => IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254;
    public static Task<Collection> Collect(Options o, CancellationToken ct)
    {
        var issues = new JsonArray(); var rows = new JsonArray();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!nic.Name.Contains(o.Get("--adapter"), StringComparison.OrdinalIgnoreCase)) continue;
            if (rows.Count >= 512) { issues.Add(new JsonObject { ["area"] = "limit", ["message"] = "512-adapter cap reached." }); break; }
            var row = new JsonObject { ["id"] = nic.Id, ["name"] = nic.Name, ["description"] = nic.Description,
                ["type"] = nic.NetworkInterfaceType.ToString(), ["status"] = nic.OperationalStatus.ToString(),
                ["addresses"] = new JsonArray(), ["gateways"] = new JsonArray(), ["dnsServers"] = new JsonArray(),
                ["configurationAvailable"] = false, ["countersAvailable"] = false, ["mtu"] = null,
                ["bytesReceived"] = null, ["bytesSent"] = null, ["incomingErrors"] = null, ["outgoingErrors"] = null };
            rows.Add(row);
            try
            {
                var ip = nic.GetIPProperties();
                row["addresses"] = J.Array(ip.UnicastAddresses.Select(a => a.Address.ToString()));
                row["gateways"] = J.Array(ip.GatewayAddresses.Select(a => a.Address.ToString()));
                row["dnsServers"] = J.Array(ip.DnsAddresses.Select(a => a.ToString()));
                row["configurationAvailable"] = true;
                row["mtu"] = ip.GetIPv4Properties()?.Mtu ?? ip.GetIPv6Properties()?.Mtu;
            }
            catch (Exception ex) when (ex is NetworkInformationException or NotSupportedException)
            {
                if (!J.Bool(row, "configurationAvailable"))
                    foreach (var key in new[] { "addresses", "gateways", "dnsServers" }) row[key] = new JsonArray();
                issues.Add(new JsonObject { ["area"] = nic.Name + "/configuration", ["message"] = ex.Message });
            }
            try
            {
                var stats = nic.GetIPStatistics();
                row["bytesReceived"] = stats.BytesReceived; row["bytesSent"] = stats.BytesSent;
                row["incomingErrors"] = stats.IncomingPacketsWithErrors; row["outgoingErrors"] = stats.OutgoingPacketsWithErrors;
                row["countersAvailable"] = true;
            }
            catch (Exception ex) when (ex is NetworkInformationException or NotSupportedException)
            { issues.Add(new JsonObject { ["area"] = nic.Name + "/counters", ["message"] = ex.Message }); }
        }
        return Task.FromResult(new Collection(new JsonObject { ["filter"] = o.Get("--adapter"), ["adapters"] = rows }, issues));
    }
    public static void ValidateEvidence(JsonObject e)
    {
        V.Text(e, "filter"); V.Rows(e, "adapters", 512);
        foreach (var row in J.Rows(e, "adapters"))
        {
            foreach (var key in new[] { "id", "name", "description", "type", "status" }) V.Text(row, key);
            foreach (var key in new[] { "addresses", "gateways", "dnsServers" })
            {
                V.Strings(row, key);
                if (J.Strings(row, key).Any(s => !IPAddress.TryParse(s, out _))) throw new ArgumentException($"Invalid IP in '{key}'.");
            }
            V.Bool(row, "configurationAvailable"); V.Bool(row, "countersAvailable");
            V.Real(row, "mtu", 0, int.MaxValue, true);
            foreach (var key in new[] { "bytesReceived", "bytesSent", "incomingErrors", "outgoingErrors" }) V.Real(row, key, 0, long.MaxValue, !J.Bool(row, "countersAvailable"));
            if (!J.Bool(row, "configurationAvailable") && new[] { "addresses", "gateways", "dnsServers" }.Any(k => J.Strings(row, k).Any()))
                throw new ArgumentException("Unavailable configuration must not contain addresses.");
        }
    }
    public static List<Finding> Analyze(JsonObject e)
    {
        var result = new List<Finding>();
        var rows = J.Rows(e, "adapters").ToArray();
        var up = rows.Where(x => J.Text(x, "status") == "Up" && J.Text(x, "type") != "Loopback").ToArray();
        if (rows.Length == 0) result.Add(new("no-adapters", "warning", "No adapters matched", "The filter matched no interfaces, or no interfaces were enumerated.", "Review the filter and collection issues."));
        else if (up.Length == 0) result.Add(new("no-up-adapter", "warning", "No non-loopback adapter is Up in this scope", "A disconnected or filtered-out interface can explain this result.", "Check the selected adapter and physical or virtual connection."));
        foreach (var row in up.Where(x => J.Bool(x, "configurationAvailable")))
        {
            var name = J.Text(row, "name"); var addresses = J.Strings(row, "addresses").ToArray();
            if (addresses.Any(IsApipa)) result.Add(new("ipv4-link-local", "warning", "Adapter has an IPv4 link-local address", name + ": 169.254/16 may indicate DHCP fallback or intentional local-only configuration.", "Check expected DHCP/static settings and connectivity; this alone does not prove DHCP failed."));
            if (!addresses.Any(IsUsable)) result.Add(new("no-routable-address", "warning", "Up adapter lacks a non-link-local unicast address", name + ": only local/unspecified addresses, or no addresses, were collected.", "Check intended adapter configuration; some virtual or local-only links need no routed address."));
            if (!J.Strings(row, "gateways").Any(s => IPAddress.TryParse(s, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))) result.Add(new("no-gateway", "info", "No gateway reported for adapter", name + ": this may be expected for a local-only interface, VPN, or routed setup.", "Inspect the route table separately if remote destinations fail; adapter gateway data is not a routing-table analysis."));
            if (!J.Strings(row, "dnsServers").Any()) result.Add(new("no-dns-configured", "info", "No DNS server reported for adapter", name + ": another adapter or local resolver may still provide name resolution.", "Compare with the intended configuration and use win-dns-doctor for actual DNS resolution tests."));
        }
        foreach (var row in rows.Where(x => J.Bool(x, "countersAvailable") && (J.Number(x, "incomingErrors") > 0 || J.Number(x, "outgoingErrors") > 0)))
            result.Add(new("interface-error-counters", "info", "Interface error counters are nonzero", $"{J.Text(row, "name")}: receive {J.Text(row, "incomingErrors")}, transmit {J.Text(row, "outgoingErrors")}.", "Compare another snapshot. These are cumulative counters, not a measured error rate or proof of a current fault."));
        return result;
    }
    public static IEnumerable<string> Describe(JsonObject e)
    {
        foreach (var row in J.Rows(e, "adapters"))
        {
            yield return $"{J.Text(row, "name")} [{J.Text(row, "type")}] {J.Text(row, "status")} | MTU {J.Text(row, "mtu", "n/a")}";
            foreach (var field in new[] { "addresses", "gateways", "dnsServers" }) yield return $"  {field}: {(J.Bool(row, "configurationAvailable") ? string.Join(", ", J.Strings(row, field)) : "unavailable")}";
            yield return $"  Counters: RX {J.Text(row, "bytesReceived", "n/a")} bytes / TX {J.Text(row, "bytesSent", "n/a")} bytes; errors RX {J.Text(row, "incomingErrors", "n/a")} / TX {J.Text(row, "outgoingErrors", "n/a")}";
        }
    }
    public static string[] SmokeArgs => [];
}
