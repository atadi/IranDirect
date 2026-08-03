# Baseline environment template

Copy this template and fill it in when recording a performance baseline. A
baseline is only comparable against a run on the **same machine and same
environment** using full mode.

## Machine

| Item | Value |
| --- | --- |
| Machine name | |
| CPU model | |
| CPU cores / threads | |
| CPU base clock | |
| RAM | |
| Storage type (SSD/HDD/NVMe) | |
| OS | |
| Windows version / build | |

## Runtime

| Item | Value |
| --- | --- |
| .NET SDK version | `dotnet --version` |
| .NET runtime version | `dotnet --list-runtimes` |
| JIT mode (TieredPGO, ReadyToRun, etc.) | `DOTNET_` env / runsettings |
| `dotnet nuget locals` state | |

## Benchmark run

| Item | Value |
| --- | --- |
| Commit hash | `git rev-parse HEAD` |
| Branch | |
| Launch mode | full (`--filter *`) |
| Date / time | |
| Host CPU affinity / priority | |
| Other processes running | |
| Power plan | |
| Hypervisor (VM? WSL2? bare metal?) | |

## Notes

- Record at least one full baseline per machine before any optimization work.
- If hardware or OS changes, record a new baseline.
- Attach the `BenchmarkDotNet.Artifacts` folder or export `--exporters json`
  output alongside this template.
