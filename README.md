# win-adapter-doctor

Inspect network adapters, addresses, gateways, DNS configuration, and interface counters.

Read-only C# / .NET 10 troubleshooting CLI for Windows 11 and Windows Server
2022/2025. No third-party runtime packages. Text and structured JSON output use
the same evidence and findings. Saved JSON can be re-analyzed offline on any
platform supported by .NET 10.

## Run

Download `win-adapter-doctor-win-x64` from a successful [GitHub Actions run](https://github.com/b69ca/win-adapter-doctor/actions),
extract it, and run `win-adapter-doctor.exe` in a terminal. The self-contained executable
includes the runtime. Initial binaries are unsigned. Build from source if preferred:

```powershell
dotnet run --project WinAdapterDoctor.csproj -c Release -- --help
dotnet run --project WinAdapterDoctor.csproj -c Release -- --json > report.json
dotnet run --project WinAdapterDoctor.csproj -c Release -- --input report.json
```

## Common interface

- `--json`: complete evidence report on stdout, including collection issues and findings.
- `--input FILE`: replay this tool's schemaVersion 1 JSON report without live queries.
  Cannot be combined with live collection options. Maximum input size: 8 MiB.
- `--help`: usage and exit-code reference.

Exit codes: 0 = report produced (including warnings or partial collection);
2 = invalid usage/snapshot; 3 = collection failed; 4 = live collection requires
Windows; 130 = cancelled. Findings do not change exit status. Check JSON `status`
and `issues` before treating a report as complete. Invalid usage is written to
stderr even with `--json`; collection failures can produce a failed JSON envelope.

Snapshots include schemaVersion, tool, version, collection/analysis times,
platform, mode, status, evidence, issues, and findings. Offline analysis preserves
the collection timestamp and recomputes findings. Editing snapshots changes the
evidence; these files are not cryptographically authenticated.

## Options and interpretation

`--adapter TEXT` selects interface names containing TEXT (literal, case-insensitive,
1–256 characters). The default includes all interfaces, including virtual, down,
and loopback adapters. Results include address lists, gateway and DNS configuration,
MTU when available, byte counters, and receive/transmit error counters. No MAC
addresses are collected. At most 512 adapters are retained.

Checks flag IPv4 link-local 169.254/16, Up non-loopback adapters without a usable
non-link-local unicast address, absent gateway/DNS configuration, and nonzero error
counters. A link-local address can be intentional. A local-only network need not
have a gateway or DNS server; other interfaces may provide them. Counters are
cumulative, not rates, and may reset. Down adapters do not receive address/DNS
warnings. Unavailable configuration is not interpreted as empty configuration.

The tool does not resolve hostnames, ping, test internet access, analyze route
selection/metrics, inspect DHCP leases, detect duplicate addresses, or prove DNS
reachability. Use the existing win-dns-doctor or win-tls-doctor for active tests.
IPv6 router information varies by platform; a missing reported gateway is not
proof that no default route exists.

```powershell
.\win-adapter-doctor.exe --adapter Ethernet
.\win-adapter-doctor.exe --json > adapters.json
```

Technical reference: [.NET NetworkInterface](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterface?view=net-10.0).

## Privacy and troubleshooting

No configuration changes, remediation, telemetry, or external network calls.
Reports stay on stdout unless you redirect them. Reports can reveal process,
network, or task names, addresses, and system configuration. Review and redact
before sharing. Synthetic test fixtures contain no private host data.

Run initially without elevation. Permission failures remain explicit; rerun with
appropriate privileges only if needed. No matching rule does not prove a healthy
system. Read the scope and limitations above before interpreting a finding.
Ctrl+C cancels collection where the underlying API permits it. Synchronous OS
API calls may delay cancellation. Remove the downloaded folder to uninstall.

## Build and test

Requires the .NET 10 SDK (global.json permits the latest installed 10.0 feature band).

```sh
dotnet build WinAdapterDoctor.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
dotnet run --project WinAdapterDoctor.csproj -c Release -- --input tests/fixture.json --json
dotnet publish WinAdapterDoctor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

The dependency-free test executable exercises diagnostic decisions, boundaries,
malformed evidence, option validation, and CLI replay. CI runs on Ubuntu and
Windows; Windows adds live read-only collector smoke checks, PowerShell syntax
validation when applicable, standalone publishing, and executable replay. A live
smoke test establishes API compatibility, not coverage of every real-world failure.

## License

MIT — see [LICENSE](LICENSE).
