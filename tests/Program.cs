using System.Text.Json.Nodes;
using WinDiagnostics;
int passed = 0, failed = 0;
void Assert(bool value, string why) { if (!value) throw new Exception(why); }
void Reject(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid evidence/options rejection"); }
void Check(string name, Action body) { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); } }
JsonObject Snapshot() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixture.json")))!;
JsonObject Evidence() => (JsonObject)Snapshot()["evidence"]!.DeepClone();
bool Has(JsonObject e, string id) => Tool.Analyze(e).Any(f => f.Id == id);
Check("snapshot replay retains evidence and rejects wrong tool/schema", () => {
    var s = Snapshot(); var c = Core.ReadSnapshot(s); Assert(c.Issues.Count == 0, "fixture issues");
    c.Evidence["testOnly"] = true; Assert(s["evidence"]!["testOnly"] is null, "clone isolation");
    s["tool"] = "other"; Reject(() => Core.ReadSnapshot(s)); s = Snapshot(); s["schemaVersion"] = 2; Reject(() => Core.ReadSnapshot(s));
    s = Snapshot(); s["issues"] = null; Reject(() => Core.ReadSnapshot(s));
});
Check("strict CLI and offline/live option separation", () => {
    Reject(() => Options.Parse(["--unknown"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--input"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--json", "--json"], Tool.ValueOptions, Tool.Flags));
    Reject(() => Options.Parse(["--input", "f", Tool.ValueOptions[0], "x"], Tool.ValueOptions, Tool.Flags));
    Tool.ValidateOptions(Options.Parse([], Tool.ValueOptions, Tool.Flags));
});
Check("malformed rows are rejected instead of silently analyzed", () => {
    Reject(() => Tool.ValidateEvidence(new JsonObject()));
    var e = Evidence(); e["adapters"] = new JsonArray { 7 }; Reject(() => Tool.ValidateEvidence(e));
});

Check("APIPA and IPv6 link local are not routed addresses", () => { Assert(Tool.IsApipa("169.254.2.3"),"APIPA"); Assert(!Tool.IsApipa("169.253.2.3"),"prefix"); Assert(!Tool.IsUsable("fe80::1%4")&&!Tool.IsUsable("169.254.2.3"),"local-only"); });
Check("private and IPv6 global addresses are usable", () => { Assert(Tool.IsUsable("10.0.0.2")&&Tool.IsUsable("fd00::1")&&Tool.IsUsable("2001:db8::1"),"addresses"); });
Check("loopback unspecified multicast and invalid IP are excluded", () => { foreach(var s in new[]{"127.0.0.1","::1","0.0.0.0","::","ff02::1","224.0.0.1","garbage"}) Assert(!Tool.IsUsable(s),s); });
Check("local-only configuration produces scoped findings", () => { var e=Evidence(); foreach(var id in new[]{"ipv4-link-local","no-routable-address","no-gateway","no-dns-configured","interface-error-counters"}) Assert(Has(e,id),id); });
Check("healthy adapter avoids configuration warnings", () => { var e=Evidence(); var a=e["adapters"]![0]!; a["addresses"]=new JsonArray("192.168.1.10"); a["gateways"]=new JsonArray("192.168.1.1"); a["dnsServers"]=new JsonArray("192.168.1.1"); a["incomingErrors"]=0; Assert(Tool.Analyze(e).Count==0,"unexpected finding"); });
Check("down adapter does not receive APIPA configuration findings", () => { var e=Evidence(); e["adapters"]![0]!["status"]="Down"; Assert(!Has(e,"ipv4-link-local")&&Has(e,"no-up-adapter"),"down"); });
Check("empty scope is explicit", () => { var e=Evidence(); e["adapters"]=new JsonArray(); Assert(Has(e,"no-adapters"),"empty"); });
Check("unavailable evidence is not interpreted as empty configuration", () => { var e=Evidence(); var a=e["adapters"]![0]!; a["configurationAvailable"]=false; foreach(var key in new[]{"addresses","gateways","dnsServers"}) a[key]=new JsonArray(); Tool.ValidateEvidence(e); Assert(!Has(e,"no-routable-address")&&!Has(e,"no-dns-configured"),"unknown is not empty"); });
Check("malformed IP and typed counters are rejected", () => { var e=Evidence(); e["adapters"]![0]!["dnsServers"]=new JsonArray("bad ip"); Reject(()=>Tool.ValidateEvidence(e)); e=Evidence(); e["adapters"]![0]!["incomingErrors"]="unknown"; Reject(()=>Tool.ValidateEvidence(e)); });
Check("blank and oversized filters rejected", () => { foreach(var f in new[]{" ",new string('a',257)}) Reject(()=>Tool.ValidateOptions(Options.Parse(["--adapter",f],Tool.ValueOptions,Tool.Flags))); });

Check("nonpositive Windows MTU sentinels remain unavailable", () => {
    Assert(Tool.NormalizeMtu(-1) is null && Tool.NormalizeMtu(0) is null && Tool.NormalizeMtu(null) is null, "unavailable sentinel");
    Assert(Tool.NormalizeMtu(1500) == 1500 && Tool.NormalizeMtu(int.MaxValue) == int.MaxValue, "valid MTU");
    var e = Evidence(); e["adapters"]![0]!["mtu"] = Tool.NormalizeMtu(-1); Tool.ValidateEvidence(e);
    e["adapters"]![0]!["mtu"] = -1; Reject(() => Tool.ValidateEvidence(e));
});
var capture = new StringWriter(); var oldOut = Console.Out; var oldErr = Console.Error;
try {
    Console.SetOut(capture); Console.SetError(capture);
    var code = await Core.Run(["--input", Path.Combine(AppContext.BaseDirectory, "fixture.json"), "--json"]);
    Console.SetOut(oldOut); Console.SetError(oldErr);
    Check("CLI JSON offline round-trip", () => { Assert(code == 0, "exit code"); var report = (JsonObject)JsonNode.Parse(capture.ToString())!; Assert(J.Text(report, "mode") == "offline", "mode"); Core.ReadSnapshot(report); });
} finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
if (OperatingSystem.IsWindows()) {
    try {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var options = Options.Parse(Tool.SmokeArgs, Tool.ValueOptions, Tool.Flags);
        Tool.ValidateOptions(options); var live = await Tool.Collect(options, timeout.Token); Tool.ValidateEvidence(live.Evidence);
        Assert(live.Evidence["adapters"] is JsonArray, "missing live rows");
        Assert(((JsonArray)live.Evidence["adapters"]!).Count > 0, "expected live evidence");
        passed++; Console.WriteLine($"PASS Windows live collection ({((JsonArray)live.Evidence["adapters"]!).Count} rows, {live.Issues.Count} issues)");
    } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL Windows live collection: " + e); }
}
Console.WriteLine($"{passed} passed; {failed} failed"); return failed == 0 ? 0 : 1;
