# Changelog

## 1.0.0 — 2026-10-27

- Initial release: Inspect network adapters, addresses, gateways, DNS configuration, and interface counters.
- Text and JSON reports, offline snapshot replay, behavior tests, and Windows CI builds.

- Treat nonpositive Windows MTU sentinel values as unavailable; verified by a regression test after Windows CI exposed a loopback-interface value.
