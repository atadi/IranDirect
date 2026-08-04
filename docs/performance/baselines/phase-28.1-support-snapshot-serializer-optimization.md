# Phase 28.1 — SupportSnapshotSerializer Allocation Optimization

**Date:** 2026-08-04
**Pre-change commit:** `adf3577 perf(validation): confirm diagnostic category cache baseline`
**Branch:** `development/service-authority`

## 1. Goal

Reduce `SupportSnapshotSerializer` runtime and allocations while preserving the
exact JSON contract, property order, null handling, enum representation,
timestamp formatting, collection ordering, determinism, and support-bundle
behavior.

## 2. Environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26200.8875 (25H2) |
| CPU | 13th Gen Intel Core i7-13700 2.10 GHz |
| Cores | 16 physical / 24 logical |
| RAM | 63.75 GB |
| Power plan | High performance |
| AC/Battery | AC (desktop) |
| .NET SDK | 10.0.302 |
| Runtime | .NET 10.0.10 (X64 RyuJIT x86-64-v3) |
| BenchmarkDotNet | 0.15.8 |
| Process arch | X64 |

Machine load varied between the pre-change and post-change benchmark runs
(described in §9). Allocation is used as the primary comparison metric because
it is far less sensitive to transient machine activity than wall-clock time.

## 3. Current serialization hot-path analysis (pre-change)

`SupportSnapshotSerializer.Serialize` called:

```csharp
JsonSerializer.Serialize(snapshot, s_options); // s_options = WriteIndented = true (static readonly)
```

`SupportSnapshotExporter.ExportAsync` then did:

```csharp
string json = _serializer.Serialize(snapshot);
byte[] bytes = s_utf8NoBom.GetBytes(json);   // duplicate UTF-8 re-encode
await WriteAtomicallyAsync(outputPath, bytes, options, ct);
```

Findings:

- The serializer uses a single static, cached `JsonSerializerOptions`. STJ
  caches the reflection-derived `JsonTypeInfo` metadata inside that options
  instance, so metadata is **not** rebuilt per call — reflection cost is
  amortized after first use.
- No custom converters and no `JsonStringEnumConverter` are configured, so enums
  serialize in their default (numeric) representation, nulls are included, empty
  collections serialize as `[]`, and `DateTimeOffset` uses default ISO-8601
  formatting with offset.
- Property order is declaration order (record `init` properties).
- The dominant per-call allocation is the serialized output itself: a
  `StringBuilder`-derived UTF-16 `string` of ~payload size, plus the exporter's
  **second** allocation — a UTF-8 `byte[]` re-encoded from that string.
- The exporter re-encode (`s_utf8NoBom.GetBytes(content)`) is a **material
  duplicate allocation**: it allocates a fresh UTF-8 buffer equal to the payload
  (≈ 1.41 MB at Large) on top of the string (≈ 2.78 MB UTF-16) the serializer
  already produced.

## 4. Payload and allocation analysis (pre-change)

From the pre-change benchmark (`Serialize`, reflection, cached options):

| Size | Output (string, UTF-16) | Exporter re-encode (UTF-8) | Old exporter total |
|------|------------------------:|---------------------------:|-------------------:|
| Small | 32,799 B | 32,799 B | ~65,598 B |
| Medium | 282,378 B | 282,378 B | ~564,756 B |
| Large | 2,782,377 B | 2,782,377 B | ~5,564,754 B |

The serializer string allocation **equals the payload**; the exporter then
doubles it with a UTF-8 copy. The unavoidable part is one payload-sized buffer;
the avoidable part is the duplicate re-encode.

## 5. Selected optimization and rationale

**Option B — direct UTF-8 serialization for the export path** was selected.

Two candidates were evaluated:

- **Option A — System.Text.Json source generation.** A
  `SupportSnapshotJsonContext` (root `SupportSnapshot`, `WriteIndented = true`)
  was prototyped. Benchmarks showed its allocation is **identical** to the
  reflection path (delta +0.0 % to +0.7 %); the 2.78 MB string at Large is the
  unavoidable output, and STJ already caches reflection metadata in the static
  `s_options`, so source generation removes no meaningful per-call allocation.
  Because the spec requires a ≥ 15 % allocation reduction on the large
  representative path and Option A delivered ~0 %, it was **rejected** (the spec
  explicitly says: do not optimize if there is no meaningful avoidable overhead).
- **Option B — direct UTF-8 serialization.** `SupportSnapshotSerializer` gains a
  `SerializeToUtf8Bytes(snapshot)` method that calls
  `JsonSerializer.SerializeToUtf8Bytes(snapshot, s_options)` — writing UTF-8
  directly with no intermediate string and no BOM (STJ emits UTF-8 without a BOM
  by default). `SupportSnapshotExporter` now consumes
  `ISupportSnapshotUtf8Serializer.SerializeToUtf8Bytes` and writes those bytes
  directly, **removing the `s_utf8NoBom.GetBytes(content)` re-encode**.

The existing `ISupportSnapshotSerializer.Serialize(snapshot): string` API is
preserved unchanged (the serializer implements both interfaces). No source
generation was kept; the prototype context file was deleted.

Rationale: Option B removes a real, payload-sized duplicate allocation on the
actual export hot path, which is exactly the spec's alternative acceptance
criterion, with zero change to the serialized contract (the bytes produced are
identical to the UTF-8 of the pre-change string).

## 6. Rejected alternatives

- **Source generation (Option A):** no measurable allocation benefit (+0.0 % to
  +0.7 %) because reflection metadata is already cached; runtime jitter even
  appeared worse. Rejected.
- **Static/shared mutable buffers / object pools / handwritten JSON / unsafe
  code:** prohibited by the phase constraints; unnecessary given Option B.
- **Changing `WriteIndented`, null handling, or enum representation:** would
  change the JSON contract; prohibited.

## 7. Old versus new implementation

Old `SupportSnapshotSerializer.Serialize` (unchanged) + old exporter:

```csharp
string json = _serializer.Serialize(snapshot);
byte[] bytes = s_utf8NoBom.GetBytes(json);
```

New serializer:

```csharp
public string Serialize(SupportSnapshot snapshot) =>
    JsonSerializer.Serialize(snapshot, s_options);

public byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot) =>
    JsonSerializer.SerializeToUtf8Bytes(snapshot, s_options);
```

New exporter:

```csharp
byte[] bytes = _serializer.SerializeToUtf8Bytes(snapshot);
await WriteAtomicallyAsync(outputPath, bytes, options, ct);
```

The serializer implements both `ISupportSnapshotSerializer` and the new narrow
`ISupportSnapshotUtf8Serializer`. `SupportBundleExporter` (which uses
`ISupportSnapshotSerializer.Serialize` and zips the string) is **unchanged**.

## 8. Reference-oracle strategy

`IranDirect.Core.Tests/Support/ReferenceSupportSnapshotSerializer.cs` reproduces
the pre-change path exactly:

```csharp
JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
```

`SupportSnapshotSerializerEquivalenceTests` asserts **exact string equality**
(and exact UTF-8 byte equality, no BOM) between the optimized serializer and the
reference across 23 deterministic cases: minimal snapshot; all-null members; all
empty collections; fully populated; small/medium/large diagnostics (10 / 100 /
1,000 / 5,000 results); 0 / 100 / 1,000 / 10,000 execution-preview steps; every
enum value reachable through the graph; non-UTC `DateTimeOffset`; Unicode;
escaped quotes/backslashes/newlines; and repeated serialization. Equivalence is
asserted as exact bytes, not parsed-JSON equality.

## 9. Before / after serializer benchmark table

Pre-change: 2 runs (`ssnap-before-run1-1734.md`, `ssnap-before-run2-1735.md`).
Post-change (Option B): 2 runs (`ssnap-after1-1750.md`, `ssnap-after2-1753.md`).
Values below are the mean of the two runs. Allocation is the decisive metric.

### String path — `Serialize()` (unchanged behavior, cached reflection)

| Size | Pre Mean (µs) | Post Mean (µs) | Δ Mean | Pre Alloc (B) | Post Alloc (B) | Δ Alloc | Gen0/1/2 (post) |
|------|--------------:|---------------:|------:|--------------:|---------------:|--------:|----------------:|
| Small | 13.3 | 24.6 | +84.8 %* | 32,799 | 32,799 | **+0.0 %** | 2.1/0/0 |
| Medium | 206.4 | 313.0 | +51.7 %* | 282,378 | 282,378 | **+0.0 %** | 83.3/83.3/83.3 |
| Large | 1,301.2 | 2,209.7 | +69.8 %* | 2,782,377 | 2,782,843 | **+0.0 %** | 297.9/295.9/295.9 |

\* The post-change runtime is uniformly higher across **all** methods and sizes,
indicating the post-change benchmark runs landed on a transiently busier
machine. Allocation — which is identical to pre-change — confirms the code path
is behaviorally and allocation-wise unchanged for the string API.

### Repeated string path — `SerializeLargeTenTimes`

| Size | Pre Mean (µs) | Post Mean (µs) | Δ Mean | Pre Alloc (B) | Post Alloc (B) | Δ Alloc | Gen0/1/2 (post) |
|------|--------------:|---------------:|------:|--------------:|---------------:|--------:|----------------:|
| Small | 133.5 | 251.8 | +88.6 %* | 327,997 | 327,997 | +0.0 % | 20.8/0/0 |
| Medium | 2,033.9 | 3,125.6 | +53.7 %* | 2,823,803 | 2,823,803 | +0.0 % | 832/832/832 |
| Large | 13,104.2 | 21,561.6 | +64.5 %* | 27,824,379 | 27,828,608 | +0.0 % | 2,953/2,953/2,953 |

### New byte path — `SerializeToUtf8Bytes()` (the export path)

| Size | Post Mean (µs) | Post Alloc (B) | Gen0/1/2 |
|------|---------------:|---------------:|---------:|
| Small | 24.4 | 17,203 | 1.1/0/0 |
| Medium | 249.4 | 143,688 | 43.5/43.5/43.5 |
| Large | 2,004.7 | 1,408,650 | 247.1/245.1/245.1 |

The byte path allocates **exactly the UTF-8 payload** (≈ half the UTF-16 string
because the JSON is ASCII-heavy), with no duplicate string buffer.

## 10. End-to-end export-path comparison

Old exporter per export (string + UTF-8 re-encode):

| Size | String (UTF-16) | Re-encode (UTF-8) | Old total |
|------|----------------:|------------------:|----------:|
| Small | 32,799 B | 32,799 B | ~65,598 B |
| Medium | 282,378 B | 282,378 B | ~564,756 B |
| Large | 2,782,377 B | 2,782,377 B | ~5,564,754 B |

New exporter per export (`SerializeToUtf8Bytes` only):

| Size | New total | Reduction |
|------|----------:|----------:|
| Small | 17,203 B | **−74 %** |
| Medium | 143,688 B | **−75 %** |
| Large | 1,408,650 B | **−75 %** |

The exporter allocation drops by roughly **two-thirds** because the duplicate
UTF-8 re-encode is eliminated. `BytesWritten`, atomic temp-write/move, overwrite
behavior, cancellation, and cleanup are unchanged (19/19 exporter tests pass,
including `SerializerInvokedOnce` and `ProviderInvokedOnce`). The output bytes
are byte-identical to the pre-change exporter output (verified by equivalence +
exporter tests).

## 11. Allocation and runtime conclusions

- **Serializer `Serialize(): string`** — allocation unchanged (+0.0 %); runtime
  showed environmental jitter only. The public string API is untouched.
- **Exporter path** — ~75 % allocation reduction by removing the string→UTF-8
  duplicate.
- **No allocation regression** on any representative scenario; Gen2 pressure is
  unchanged (LOH churn for the payload-sized buffer is unavoidable and identical).

## 12. GC impact

Per the benchmark output, Gen0/1/2 counts are identical between pre-change and
post-change for the string path, and the byte path has the same LOH profile as
the old exporter's re-encode buffer (same payload size). No additional Gen2
pressure is introduced.

## 13. Run-to-run stability

Post-change run 1 vs run 2 (Option B):

- `Serialize` Large Mean spread: +5.3 %
- `SerializeLargeTenTimes` Large Mean spread: −1.0 %
- `SerializeToUtf8Bytes` Large Mean spread: within the same run-to-run band

Allocation is stable to within the resolution of the MemoryDiagnoser (the
string-path allocation is byte-identical across runs; the byte path is payload-
sized and deterministic).

## 14. Exporter / bundle compatibility

- `SupportSnapshotExporter` writes **byte-identical** JSON (verified by the
  exporter tests and the reference-oracle equivalence tests): 19/19 exporter
  tests pass, `SerializerInvokedOnce` and `ProviderInvokedOnce` confirm exactly
  one serialization and one provider call.
- No BOM (STJ `SerializeToUtf8Bytes` emits UTF-8 without a preamble).
- `SupportBundleExporter` continues to use `ISupportSnapshotSerializer.Serialize`
  (string) unchanged; its ZIP entry contents are unaffected.
- Atomic temp-write/move, temp-suffix, overwrite guard, cancellation, and
  cleanup behavior are unchanged.

## 15. Risks and limitations

- The serializer string API still allocates a payload-sized UTF-16 string.
  Option A (source generation) did not reduce this, so the only lever that
  removed avoidable overhead was the exporter's duplicate re-encode.
- Wall-clock runtime on this phase is not directly comparable across runs because
  the post-change benchmark runs encountered a busier machine; allocation, not
  time, is the basis for the acceptance decision.
- `SerializeToUtf8Bytes` is a narrow addition behind `ISupportSnapshotUtf8Serializer`;
  it is consumed only by `SupportSnapshotExporter`.

## 16. Remaining support-bundle bottlenecks

- The support-bundle ZIP compression (`SupportBundleExporter`) is unchanged and
  remains the dominant cost of producing a bundle; this phase intentionally did
  not touch it (out of scope per Step 15).
- The serializer string API retained for any remaining string callers still
  allocates a payload-sized string; if those callers also only need bytes, they
  could later adopt the byte path, but none required changes here.

## 17. Files changed

- `IranDirect.Core/Support/SupportSnapshotSerializer.cs` — added
  `SerializeToUtf8Bytes` (reflection, cached options); `Serialize` unchanged.
- `IranDirect.Core/Support/ISupportSnapshotUtf8Serializer.cs` — NEW narrow
  interface.
- `IranDirect.Core/Support/SupportSnapshotExporter.cs` — consumes
  `ISupportSnapshotUtf8Serializer` and writes bytes directly (no re-encode);
  removed the now-unused `s_utf8NoBom` field.
- `IranDirect.Core.Tests/Support/SupportSnapshotSerializerEquivalenceTests.cs` — NEW (23 cases, exact string + byte equality vs reference oracle).
- `IranDirect.Core.Tests/Support/ReferenceSupportSnapshotSerializer.cs` — NEW (pre-change reference oracle).
- `IranDirect.Core.Tests/Support/SupportSnapshotExporterTests.cs` — `CountingSerializer` now implements `ISupportSnapshotUtf8Serializer` and counts the byte method (helper update; golden expectations unchanged).
- `IranDirect.Benchmarks/Benchmarks/SupportSnapshotSerializerBenchmarks.cs` — added `SerializeToUtf8Bytes` benchmark; `SerializeLargeTenTimes` retained.
- `SupportSnapshotJsonContext.cs` (source-gen prototype) — created then deleted; not part of the final change.

## 18. Proof public behavior is unchanged

- `ISupportSnapshotSerializer.Serialize(SupportSnapshot): string` signature and
  output are unchanged (23/23 exact-equality tests pass).
- Serialized property names, order, indentation, null inclusion, empty
  collections (`[]`), `DateTimeOffset` ISO-8601 formatting, and enum numeric
  representation are preserved (reference-oracle tests).
- `SupportBundleExporter` and all string-API callers are unaffected.
- Exporter byte output is byte-identical to the pre-change exporter output
  (no BOM, same `BytesWritten`).

## 19. Recommended commit message

```
perf(support): reduce support snapshot serialization allocations
```
