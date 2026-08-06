# Runbook: IranDirectPrometheusStoragePressure (+ host pressure alerts)

- **Alerts:**
  - `IranDirectPrometheusStoragePressure` (warning, host root free < 15% / 15m)
  - `IranDirectPrometheusStoragePressureCritical` (critical, host root free < 5% / 5m)
  - `IranDirectHostMemoryPressure` (warning, host memory used > 90% / 15m)
  - `IranDirectHostFilesystemInodesLow` (warning, host root inodes < 10% / 15m)
- **Severity:** warning / critical (storage); warning (memory, inodes)
- **Component:** prometheus / infrastructure
- **Dashboard:** `irandirect-reliability-errors`
- **Prometheus queries:**
  - `node_filesystem_avail_bytes{mountpoint="/"} / node_filesystem_size_bytes{mountpoint="/"}`
  - `1 - node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes`
  - `node_filesystem_files_free{mountpoint="/"} / node_filesystem_files{mountpoint="/"}`

## What it means
The host filesystem holding the named Docker volumes (prometheus-data,
tempo-data, grafana-data, alertmanager-data) is running low on space, memory, or
inodes. These alerts are produced by **node-exporter** (Phase 33.5, production
overlay only) and target **only the host root filesystem** — not every mount.

## User impact
- **Storage pressure:** old Prometheus blocks are evicted when the size cap is
  hit, silently shortening effective retention; if it reaches 0%, ingestion
  halts. Tempo block writes can also fail.
- **Memory pressure:** backend components may be OOM-killed.
- **Inodes low:** even with free space, new files (TSDB/trace blocks) cannot be
  created.

## Dashboard
Open the `irandirect-reliability-errors` dashboard. For live host signals query
Prometheus directly (above). node-exporter is scraped only in the production
overlay; these alerts will not fire on the local base stack.

## Symptoms
Grafana panels for host metrics flatline or show rising usage; `docker system
df` shows the irandirect-*-data volumes consuming most of the host disk.

## Likely causes
- Telemetry growth exceeding the 30d/40GB cap without expansion.
- A leak in a backend component consuming memory.
- Inode-heavy directories (e.g. many small trace blocks) on a small FS.

## Safe checks
`docker system df`; inspect the irandirect-prometheus-data volume size; confirm
the host root FS free% via `df -h /`; check `free -m` / `node_memory_*`; check
`df -i /` for inodes. Do NOT delete live volume data to "fix" space.

## Corrective actions
- Storage: expand the host volume / add disk; or temporarily lower
  `--storage.tsdb.retention.time` (and the size cap) to evict older data; or
  raise the size cap if the host has headroom.
- Memory: identify the leaking component (`docker stats`); resize the host or
  the `deploy.resources` limits; restart the offender.
- Inodes: locate inode-heavy directories and clean up; expanding the FS also
  expands the inode table on most filesystems.

## What not to do
Do not add node-exporter to the base/local stack (it is a production-overlay
concern). Do not fabricate a storage metric. Do not wipe volumes to recover
space without a backup of any state you need.

## Escalation criteria
Critical storage (< 5%) or any ingestion halt: page the platform owner to
expand capacity immediately; preserve `docker system df` and volume sizes as
evidence.

## Evidence to preserve
`docker system df`, host `df -h /`, `df -i /`, `free -m`, retention settings,
Prometheus/Tempo volume sizes.

## Resolution verification
Host root free% returns above threshold (storage > 15%, inodes > 10%),
memory used < 90%, and the alert clears after its `for:` window.

## Related alerts
IranDirectPrometheusTargetDown, IranDirectCollectorUnavailable

## Ownership
Observability / Platform
