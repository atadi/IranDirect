# Phase 28.2 — SupportSnapshotSerializer Post-Optimization Validation

**Date:** 2026-08-04
**Optimized commit:** `add99ff perf(support): reduce support snapshot serialization allocations`
**Comparison (pre-change) commit:** `adf3577 perf(validation): confirm diagnostic category cache baseline`
**Branch:** `development/service-authority`

## 1. Goal

Validate the Phase 28.1 UTF-8 export optimization using repeat benchmark runs,
confirm exact output compatibility, verify exporter behavior, and adopt the
optimized implementation as the new serialization baseline. Validation and
documentation only — no production code, tests, or benchmark source modified,
and no further optimization introduced.

## 2. Environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 2.10 GHz |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance (active) |
| AC/Battery | AC (desktop) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64 RyuJIT x86-64-v3) |
| BenchmarkDotNet | 0.15.8 |
| Background load | ~506 processes at run time |

## 3. Verification (Step 3)

| Gate | Result |
|------|--------|
| `dotnet clean` | ok |
| `dotnet build` | 0 errors, 3 stale warnings |
| Full suite `dotnet test` | **1887 / 1887 passed** |
| Focused `SupportSnapshot|SupportBundle` | **145 / 145 passed** |
| Stress (`Category=Stress`) | **12 / 12 passed** |
| Benchmark Release build | 0 errors, 0 warnings |

The 3 warnings are pre-existing stale warnings unrelated to this change.

## 4. Benchmark execution (Step 4)

The `SupportSnapshotSerializerBenchmarks` class was run twice on the committed
code (HEAD = `add99ff`):

- Run 1: `BenchmarkDotNet.Artifacts/results/ssnap282-run1-1926.md` (9 benchmarks)
- Run 2: `BenchmarkDotNet.Artifacts/results/ssnap282-run2-1928.md` (9 benchmarks)

Comparison baselines:
- Pre-change (reflection string + UTF-8 re-encode): `ssnap-before-run1-1734.md`, `ssnap-before-run2-1735.md`
- Phase 28.1 post-change (Option B): `ssnap-after1-1750.md`, `ssnap-after2-1753.md`

Values below are means of the two runs. Allocation is the decisive metric;
wall-clock time is reported but is sensitive to transient machine load.

## 5. Before / after serializer table

Pre-change (reflection `Serialize` + exporter `GetBytes` re-encode) vs Phase 28.2
committed (byte path replaces the re-encode). The `Serialize` string API is
unchanged; `SerializeToUtf8Bytes` is the new export path.

| Method | Size | Pre Mean (µs) | Val Mean (µs) | Pre Alloc (B) | Val Alloc (B) | Δ Alloc | Gen0/1/2 (val) |
|--------|------|--------------:|--------------:|--------------:|--------------:|--------:|----------------:|
| Serialize | Small | 13.3 | 13.3 | 32,799 | 32,799 | **+0.0 %** | 2.1/0/0 |
| Serialize | Medium | 206.4 | 209.3 | 282,378 | 282,378 | **+0.0 %** | 83.3/83.3/83.3 |
| Serialize | Large | 1,301.2 | 1,308.5 | 2,782,377 | 2,782,418 | **+0.0 %** | 304.7/302.7/302.7 |
| SerializeLargeTenTimes | Small | 133.5 | 134.7 | 327,997 | 327,997 | +0.0 % | 20.8/0/0 |
| SerializeLargeTenTimes | Medium | 2,033.9 | 2,090.0 | 2,823,803 | 2,823,803 | +0.0 % | 832/832/832 |
| SerializeLargeTenTimes | Large | 13,104.2 | 13,037.7 | 27,824,379 | 27,824,671 | +0.0 % | 3,046.9/3,031.2/3,031.2 |
| SerializeToUtf8Bytes | Small | – | 12.5 | – | 17,203 | – | 1.1/0/0 |
| SerializeToUtf8Bytes | Medium | – | 153.8 | – | 143,688 | – | 43.5/43.5/43.5 |
| SerializeToUtf8Bytes | Large | – | 1,159.6 | – | 1,407,626 | – | 230.5/228.5/228.5 |

> "Large" in the benchmark exercises **both** large diagnostics (1,000 results)
> and large execution preview (1,000 steps) via the shared `SnapshotSize` param
> in `SupportSnapshotFactory`; the benchmark does not split them into separate
> methods, so Large is reported once as covering both dimensions.

## 6. Repeat-stability table (Phase 28.1 post-change vs Phase 28.2 committed)

| Method | Size | 28.1 Mean (µs) | 28.2 Mean (µs) | Δ Mean | 28.1 Alloc (B) | 28.2 Alloc (B) | Δ Alloc |
|--------|------|---------------:|---------------:|-------:|---------------:|---------------:|--------:|
| Serialize | Small | 24.6 | 13.3 | −46.2 %* | 32,799 | 32,799 | +0.0 % |
| Serialize | Medium | 313.0 | 209.3 | −33.1 %* | 282,378 | 282,378 | +0.0 % |
| Serialize | Large | 2,209.7 | 1,308.5 | −40.8 %* | 2,782,843 | 2,782,418 | −0.0 % |
| SerializeToUtf8Bytes | Small | 24.4 | 12.5 | −49.0 %* | 17,203 | 17,203 | +0.0 % |
| SerializeToUtf8Bytes | Medium | 249.4 | 153.8 | −38.3 %* | 143,688 | 143,688 | +0.0 % |
| SerializeToUtf8Bytes | Large | 2,004.7 | 1,159.6 | −42.2 %* | 1,408,650 | 1,407,626 | −0.1 % |

\* The Phase 28.1 post-change runs were on a transiently busier machine (the
Wall-clock numbers were uniformly higher across all methods/sizes). The Phase
28.2 re-runs are faster and, critically, the **allocation is identical** to the
Phase 28.1 post-change numbers (within −0.1 %), confirming the optimized code
path is stable and the Phase 28.1 runtime "regression" was environmental, not a
code effect.

## 7. Export-path comparison (Step 6)

Old exporter per export (pre-change): `Serialize()` string (UTF-16, payload
sized) + `s_utf8NoBom.GetBytes(content)` (UTF-8, payload sized) = ~2× payload.

New exporter per export (committed): `SerializeToUtf8Bytes()` only (UTF-8,
payload sized, no BOM) = ~1× payload.

| Size | Old exporter total (B) | New exporter total (B) | Reduction |
|------|-----------------------:|-----------------------:|----------:|
| Small | ~65,598 | 17,203 | **−74 %** |
| Medium | ~564,756 | 143,688 | **−75 %** |
| Large | ~5,564,754 | 1,407,626 | **−75 %** |

End-to-end exporter allocation is ~75 % lower, matching Phase 28.1.

### Export compatibility confirmation

- **Byte output identical** — `SupportSnapshotExporterTests` assert the written
  file equals the serializer bytes (byte-equality assertions).
- **UTF-8 identical / no BOM** — STJ `SerializeToUtf8Bytes` emits UTF-8 without a
  preamble; the exporter no longer prepends a BOM (the old `s_utf8NoBom` field was
  removed).
- **ZIP entry identical** — `SupportBundleExporter` continues to use the
  `ISupportSnapshotSerializer.Serialize` string API unchanged; its entry contents
  are unaffected.
- **File contents identical** — exporter file-equality tests pass.
- **Exporter invokes byte serializer exactly once** — `SerializerInvokedOnce`
  asserts `CallCount == 1` on the (narrow-interface) serializer.
- **Provider invocation count unchanged** — `ProviderInvokedOnce` asserts exactly
  one `CaptureAsync` call.
- **Atomic file behavior unchanged** — temp-suffix write + `File.Move` overwrite
  + cleanup-on-failure path is unmodified and covered by exporter tests.

## 8. Acceptance (Step 7)

| Criterion | Result |
|-----------|--------|
| `Serialize(string)` unchanged | Pass — allocation +0.0 %, exact output |
| Exact string equality | Pass — 23/23 equivalence tests vs reference oracle |
| Exact byte equality | Pass — UTF-8 byte-equality assertions |
| Exporter allocation reduction preserved | Pass — ~75 % lower |
| No runtime regression > 10 % | Pass — runtime neutral/better (jitter only) |
| Allocation regression ≤ 5 % | Pass — +0.0 % to −0.1 % |
| No additional Gen2 | Pass — Gen2 unchanged (LOH churn identical) |
| ServiceResponse unchanged | Pass — no IPC schema touched |
| Bundle unchanged | Pass — `SupportBundleExporter` untouched |
| Property ordering unchanged | Pass — reference-oracle equivalence |
| DateTimeOffset unchanged | Pass — ISO-8601 format preserved |
| Enums unchanged | Pass — numeric representation preserved |

## 9. Baseline adoption

The committed implementation at `add99ff` is adopted as the new support-snapshot
serialization baseline. All acceptance criteria are met; the optimization is
exact-output compatible and reduces export-path allocation by ~75 % with no
regression.

## 10. Proof no source changed in this phase

Phase 28.2 modified documentation only. After writing the docs:

```
git status --short
 M docs/performance/README.md
?? docs/performance/baselines/phase-28.2-support-serializer-validation.md

git diff --name-only
docs/performance/README.md

git diff --stat
 docs/performance/README.md | 4 ++++
 1 file changed, 4 insertions(+)

git diff --check   (clean)
```

No production code, tests, or benchmark source were modified.

## 11. Files changed (this phase)

- `docs/performance/baselines/phase-28.2-support-serializer-validation.md` — NEW
- `docs/performance/README.md` — link added

## 12. Recommended commit message

```
perf(validation): confirm support serializer optimization baseline
```

Not committed automatically.
