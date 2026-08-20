# Phase 31.1 — Route identity caching design and compatibility analysis

Investigation, measurement and documentation only. No production code was
modified. A temporary test-only diagnostic was used for measurement and was
deleted before completion.

**Outcome: NO-CHANGE recommended. Do not implement route identity caching.
Planner allocation optimization should stop here.**

## 1. Commit and environment

| Item | Value |
| --- | --- |
| Commit | `ed32b95cd4be8b35fd85ed942ca3b00d6651eb9` |
| Subject | `docs(perf): record resolution of phase 30.3 build warnings` |
| Branch | `development/service-authority` |
| Working tree at start | clean |
| OS | Windows 11 Pro 10.0.26200 (25H2) |
| CPU | Intel Core i7-13700, 16P / 24L |
| RAM | 64 GB |
| Power plan | High performance |
| SDK / runtime | 10.0.302 / 10.0.10, win-x64 RyuJIT x86-64-v3 |
| Measurement | `GC.GetAllocatedBytesForCurrentThread`, Release |

## 2. Model inventory

Every production type exposing a route `Identity`:

| # | Type | File | Kind | Identity impl | Allocates per access | Serialized | Persisted | Dict/set key | `with` used |
| --- | --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| 1 | `ObservedRoute` | `IranDirect.Core/Runtime/ObservedRoute.cs:13` | `sealed record` | computed `=>` | yes | no | no | **yes** (planner dict) | yes (tests) |
| 2 | `DesiredPrefixRoute` | `IranDirect.Core/Runtime/DesiredPrefixRoute.cs:13` | `sealed record` | computed `=>` | yes | no | no | **yes** (planner set) | yes (tests) |
| 3 | `DesiredEndpointRoute` | `IranDirect.Core/Runtime/DesiredEndpointRoute.cs:21` | `sealed record` | computed `=>` | yes | no | no | **yes** (planner set) | yes (tests) |
| 4 | `ManagedRoute` | `IranDirect.Core/Routing/ManagedRoute.cs:15` | `sealed record` | computed `=>` | yes | no | no | no | no |
| 5 | `RouteInventoryItem` | `IranDirect.Core/Routing/RouteInventoryItem.cs:16` | `sealed record` | computed `=>`, **`[JsonIgnore]`** | yes | yes (item) | **yes** | no | no |
| 6 | `VpnEndpointInventoryItem` | `IranDirect.Core/Vpn/VpnEndpointInventoryItem.cs:32` | `sealed record` | computed `=>`, **`[JsonIgnore]`** | yes | yes (item) | **yes** | no | no |
| 7 | `RuntimeExecutionStep` | `.../Runtime/Execution/RuntimeExecutionStep.cs:7` | `sealed record` | **stored** `required string Identity { get; init; }` | no | no | no | no | no |
| 8 | `RuntimeChange` | `.../Runtime/Reconciliation/RuntimeChange.cs:7` | `sealed record` | **stored** `required string Identity { get; init; }` | no | no | no | no | no |

All eight are `sealed record` with `required` / `init` properties and no
primary constructors — every one is constructed via object initializers.

Types 7 and 8 already **store** identity (output payload; no caching question
arises). Types 5 and 6 are the persisted ones and are already protected from
serialization by `[JsonIgnore]`. Types 1–3 are the planner-hot ones.

**These types should not be changed together.** Only 1–3 are on the planner
hot path; 4–6 are cold; 7–8 are already stored.

## 3. Complete usage map

`.Identity` references outside `obj/`/`bin/`, by project:

| Project | References |
| --- | ---: |
| `IranDirect.Core.Tests` | 152 |
| `IranDirect.Core` | 69 |
| `IranDirect.Testing` | 27 |
| `IranDirect.Benchmarks` | 2 |

By subsystem (production only):

| Subsystem | Uses identity | Nature |
| --- | :---: | --- |
| Planning (`RuntimeChangeSetPlanner`) | yes | dictionary/set keys, step field — **hot path** |
| Reconciliation (`RuntimeReconciler`) | yes | consumes `RuntimeChange.Identity` (stored) |
| Execution (step handlers) | yes | consumes `RuntimeExecutionStep.Identity` (stored) |
| Routing (`ManagedRoute`, `WindowsRouteApi`) | yes | comparison/logging only |
| Inventory (`RouteInventoryItem`, `VpnEndpointInventoryItem`) | yes | in-memory matching only; `[JsonIgnore]` |
| Persistence (`JsonStore<T>`) | **no** | identity never written — see §6 |
| Diagnostics / observability | no | — |
| Support snapshots | **no** | serializer contains no route-identity reference |
| IPC (`ServiceResponse`) | **no** | carries `DesiredConfiguration`, custom routes, DNS statuses — none of types 1–3 |
| CLI / Tray | no | render prefix/gateway fields, not identity |
| Tests / benchmarks | yes | assertions and workload construction |

Key structural finding: **types 1–3 never enter a serialized graph.** They
appear in `IRuntimeObservationSource` and the planner only. Grepping
`IranDirect.Core/Ipc`, `IranDirect.Core/Support` and
`IranDirect.Core/Persistence` for these three types returns nothing.

## 4. Identity formulas and semantic contract

All six computed implementations are the identical interpolation:

```csharp
public string Identity => $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
```

with one field-name variation — `ObservedRoute` uses `NextHop` in the gateway
position.

| Aspect | Behavior |
| --- | --- |
| Separator | `\|` (U+007C), two occurrences |
| Field order | DestinationPrefix, Gateway/NextHop, InterfaceIndex |
| Null behavior | properties are `required`; a null string would interpolate as empty, not throw |
| Whitespace | no trimming — preserved verbatim |
| Casing | preserved in the string; **case-insensitivity comes from the consumer**, not the formula (planner uses `StringComparer.OrdinalIgnoreCase`) |
| Numeric formatting | `uint.ToString()` via interpolation |
| Culture | **culture-sensitive by construction** — `$""` uses current culture; benign for invariant-digit `uint` and ASCII prefixes, but formally not `InvariantCulture` |
| Gateway formatting | `ManagedRoute` interpolates `IPAddress.ToString()`; all others use the raw `string` |
| Prefix normalization | none — identity assumes callers supply already-normalized prefixes |
| Metric participates | **no** |
| Route kind participates | **no** — an endpoint and a prefix route with equal fields yield equal identity strings; separation is enforced by the planner's separate collections, not the identity |

Equal constructor/property values always produce equal identity strings
(pure function of three fields, no hidden state). Fields used by identity are
`init`-only, so they can only change through construction or a `with`
expression — never in-place mutation.

## 5. Record, equality and `with`-expression analysis

Current behavior, verified by diagnostic probe:

- **Equality/hash components.** These records have no primary constructor;
  generated equality covers the declared `init` properties
  (`DestinationPrefix`, `Gateway`/`NextHop`, `InterfaceIndex`, `Metric`, plus
  the endpoint/VPN extras). A computed getter with no backing field
  contributes **no** equality component. `Metric` **does** participate in
  equality but **not** in identity — so two records can be `Equals`-unequal
  yet identity-equal. Existing tests rely on exactly this
  (`PlannerEquivalenceTests.cs:217`, `:252` build `first with { Metric = ... }`
  precisely to exercise identity-equal/record-unequal duplicates).
- **`with` on the production types is correct today.** Probe:
  `p1 = 203.0.113.0/24|192.168.100.1|30`, and
  `p1 with { DestinationPrefix = "198.51.100.0/24" }` yields
  `198.51.100.0/24|192.168.100.1|30`. Source unchanged after the copy.
- **Deconstruction / serialization constructors:** none declared; all
  construction is via object initializers.

### The decisive finding — design B is unsafe

A surrogate record reproducing the lazy-cache design
(`private string? _identity; public string Identity => _identity ??= ...`)
was probed:

```
withProbe original = 203.0.113.0/24|192.168.100.1|30
withProbe mutated  = 203.0.113.0/24|192.168.100.1|30
withProbe STALE    = TRUE
```

After populating the cache and then applying
`with { DestinationPrefix = "198.51.100.0/24" }`, the copy **reported the old
identity**. The compiler-generated copy constructor copies *all* instance
fields, including the private cache, and `init` assignment happens after the
copy — so the stale value survives.

This is not theoretical: `with` is already applied to these exact types in
`PlannerEquivalenceTests.cs:217`, `PlannerEquivalenceTests.cs:252` and
`PlannerAdversarialEquivalenceTests.cs:67`. Under design B, those tests would
be silently computing identities from pre-`with` field values, corrupting
duplicate detection and route matching.

A second probe showed the cache does **not** leak into equality
(`equal=False`/`hashEqual=False` was driven by differing `DestinationPrefix`,
not cache state) — but the staleness defect alone disqualifies the design.

## 6. Serialization and persistence analysis

| Question | Finding |
| --- | --- |
| Does a getter-only `Identity` serialize today? | For types 5–6, **no** — explicit `[JsonIgnore]`. For types 1–4, moot: they are never serialized. |
| Does `Identity` appear in any persisted JSON? | **No.** Persisted stores are `JsonStore<RouteInventory>` and `JsonStore<VpnEndpointInventory>`, whose items carry `[JsonIgnore]` on `Identity`. |
| Would a backing field serialize? | Private fields are ignored by System.Text.Json by default — but design A (constructor-computed *public* get-only property) **would** begin serializing on types 5–6 unless `[JsonIgnore]` were preserved, changing persisted shape. |
| Do caches survive deserialization? | Not applicable to 1–3. For 5–6, deserialization uses the parameterless-object-initializer path, so a lazy cache would start empty — safe but pointless. |
| Property order | Unchanged by any design that keeps `[JsonIgnore]`. |
| Support snapshot compatibility | Unaffected — `SupportSnapshotSerializer` contains no route-identity reference. |
| Route inventory file compatibility | Unaffected provided `[JsonIgnore]` is retained. |
| Legacy JSON compatibility | Unaffected. |
| IPC compatibility | Unaffected — `ServiceResponse` carries none of types 1–3. |

Serialization/persistence/support tests re-run as evidence:
**101 passed, 0 failed** (`RouteInventory|SupportSnapshot|VpnEndpointInventory|Serialization`).

Serialization risk is therefore **low** for types 1–3 and **real but
manageable** for types 5–6 — and types 5–6 are not on the hot path, so there
is no reason to touch them.

## 7. Allocation attribution

Measured with `GC.GetAllocatedBytesForCurrentThread`, Release.

### Bytes per identity by model and access count

| Size | Accesses | ObservedRoute | DesiredPrefixRoute | DesiredEndpointRoute | Bytes / identity |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1K | ×1 | 87,680 | 87,680 | 87,680 | 87.7 |
| 1K | ×2 | 175,360 | 175,360 | 175,360 | 87.7 |
| 1K | ×10 | 876,800 | 876,800 | 876,800 | 87.7 |
| 10K | ×1 | 879,200 | 879,200 | 879,200 | 87.9 |
| 10K | ×2 | 1,758,400 | 1,758,400 | 1,758,400 | 87.9 |
| 10K | ×10 | 8,792,000 | 8,792,000 | 8,792,000 | 87.9 |
| 50K | ×1 | 4,399,200 | 4,399,200 | 4,399,200 | 88.0 |
| 50K | ×2 | 8,798,400 | 8,798,400 | 8,798,400 | 88.0 |
| 50K | ×10 | 43,992,064 | 43,992,000 | 43,992,000 | 88.0 |

Cost is **identical across all three model types** — identity cost is a
property of the formula, not the model. Allocation is exactly linear in
access count, confirming no interning or hidden reuse.

### Fixed versus content-dependent

| Input | Identity length | Bytes |
| --- | ---: | ---: |
| `1.1.1.1/32 \| 10.0.0.1 \| 3` | 21 chars | 64 |
| IPv6 prefix + IPv6 gateway + `uint.MaxValue` | 94 chars | 216 |

`bytes ≈ 22 + 2 × chars` — the standard .NET string header plus UTF-16
payload. Allocation is **content-dependent**, dominated by prefix/gateway
text length. IPv6-heavy deployments cost ~3.4× more per identity than IPv4.

### Collection construction (50K)

| Operation | Allocated |
| --- | ---: |
| Dictionary keyed by `ObservedRoute.Identity` | 5,865,440 B |
| HashSet keyed by `DesiredPrefixRoute.Identity` | 5,446,536 B |
| Identity strings alone (×1) | 4,399,200 B |

Identity strings are **75%** of dictionary construction and **81%** of hash-set
construction; the remainder is bucket/entry backing storage.

### Share of remaining planner allocation (50K, post-Phase-30.2)

| Scenario | Planner total | Identity strings | Share |
| --- | ---: | ---: | ---: |
| AllMissing | 16,804 KB | 4,297 KB | 25.6% |
| AllPresent | 10,746 KB | 8,594 KB | **80.0%** |
| AllObsolete | 14,264 KB | 4,297 KB | 30.1% |
| Mixed | 18,384 KB | 7,305 KB | 39.7% |
| DuplicateInput | 22,048 KB | 8,594 KB | 39.0% |

## 8. Why the large share does **not** translate into a caching benefit

This is the central conclusion of the phase.

Identity strings are 26–80% of remaining planner allocation, which looks like
a large opportunity. It is not, because **Phase 30.2 already eliminated
repeated identity reads.** The planner now reads each route's identity
exactly once:

```
 24  route => route.Identity      // observed lookup construction (per observed route)
169  string identity = route.Identity;   // endpoint add pass (per desired route)
235  string identity = route.Identity;   // prefix add pass   (per desired route)
296  Identity = route.Identity,          // removal pass, per *observed* route
137/138  x!.Identity, y!.Identity        // comparer — reads the STORED
                                         // RuntimeChange.Identity, not a route
```

Lines 169 and 235 are mutually exclusive (endpoint vs prefix passes over
disjoint collections). Line 296 operates on observed routes during removal.

A cache eliminates the *second and subsequent* reads. With exactly one read
per object, the cache is populated and immediately discarded — **saving
approximately zero**, while adding a field to every route instance.

The surrogate benchmark confirms the mechanism and its precondition: at 50K
with **two** accesses, computed cost 8,798,400 B versus lazy-cached
4,399,120 B (−50%). The saving is exactly the second read. With one read there
is nothing to save.

Could reuse arise across cycles? No. `RuntimeCoordinator.BuildPlanAsync`
(`RuntimeCoordinator.cs:23-46`) calls `_observer.ObserveAsync` every cycle and
constructs a fresh `RuntimePlanSnapshot`. Route objects are never re-planned,
so a per-object cache has no cross-call reuse either.

**Expected planner allocation reduction from identity caching: ~0%**, against
a §13 no-change threshold of 10–15%.

## 9. Candidate design comparison

| Rank | Design | Alloc benefit | Runtime benefit | Semantic risk | Serialization risk | Scope | Test burden | Recommendation |
| ---: | --- | --- | --- | --- | --- | --- | --- | --- |
| **1** | **Keep current computed property** | baseline | baseline | **none** | **none** | none | none | **ADOPT** |
| 2 | Planner-local identity key | ~0% (already one read) | ~0% | low | none | planner only | low | Reject — Phase 30.2 already realized this |
| 3 | Constructor-computed immutable identity | ~0% in planner | ~0% | medium — no primary ctors; all construction is object-initializer, so identity cannot be computed in a ctor without redesigning every call site | medium — public get-only property would begin serializing on types 5–6 unless `[JsonIgnore]` retained | broad (every construction site) | high | Reject |
| 4 | Lazy cache with safe copy semantics | ~0% in planner | ~0% | **HIGH — proven stale identity after `with`**; a hand-written copy constructor clearing the cache is required, and hand-writing copy semantics on records is fragile and easy to regress | low for 1–3 | model types + custom copy ctors | high | **Reject — fails §11 hard gate** |
| 5 | Separate `RouteIdentity` value object | negative (adds object per route) | negative (conversion overhead) | high | high — new type in persisted graphs | architectural | very high | Reject |

Design D (caller-supplied identity) is not ranked separately: it duplicates
the source of truth and permits callers to supply an identity inconsistent
with the fields — an unacceptable correctness regression for zero measured
benefit.

## 10. Decision criteria evaluation (§11)

| Criterion | Best candidate (design B) | Result |
| --- | --- | :---: |
| Removes material share of planner allocation | ~0% — one read per object | **FAIL** |
| Cannot produce stale identity after `with` | proven stale (`STALE = TRUE`) | **FAIL** |
| Does not alter equality/hash behavior | holds (no equality component added) | pass |
| Does not alter JSON shape/property order | holds for types 1–3 | pass |
| Does not break persisted inventory files | holds if `[JsonIgnore]` retained | pass |
| No caller-supplied duplicate state | holds | pass |
| Thread-safe | benign race, but adds shared mutable state to a snapshot-shared record | marginal |
| Understandable/maintainable | custom copy constructors on records — fragile | **FAIL** |
| Validatable with reference oracle | yes | pass |
| Localized scope | model types consumed across subsystems | marginal |

Three hard failures. Per §11, **no design qualifies**.

## 11. No-change threshold (§13)

Every listed no-change condition is satisfied:

- Expected planner allocation reduction (**~0%**) is far below the 10–15%
  threshold.
- The safest workable design requires broad model/API changes (design A) or
  hand-written record copy semantics (design C).
- Equality/serialization risk plus the proven staleness defect outweigh a
  benefit measured at approximately zero.
- Remaining cost is acceptable at production scale: real deployments plan
  hundreds to low thousands of routes, not 50K. At 1K the entire identity
  cost is 87.7 KB per plan cycle.
- The apparent hotspot is overwhelmingly a **synthetic 50K workload artifact**.

## 12. Recommendation

**No change. Do not implement route identity caching. There is no Phase 31.2
implementation scope.**

Phase 30.2 already captured the identity-related win by removing duplicate
reads. What remains is the irreducible cost of materializing each identity
once — genuine output-adjacent work, not waste.

If planner allocation must fall further, the remaining levers are *not*
identity caching:

1. Shorten identity content (dominant cost is prefix/gateway text length),
   e.g. a struct key of `(prefix, gateway, ifIndex)` with a custom comparer
   and **no string materialization at all**. This avoids the staleness
   problem entirely because nothing is cached — but it is an architectural
   change to planner keying and should only be considered if a real
   production profile justifies it.
2. Accept the current floor.

Recommendation: **accept the current floor and stop planner optimization.**

## 13. Test-gap notes for any future phase

Existing coverage already establishes most current-behavior invariants
(`PlannerEquivalenceTests`, `PlannerAdversarialEquivalenceTests`, 101
serialization/inventory/support tests). Gaps that would need closing *only if*
identity storage is ever revisited:

- No permanent test asserts `with { DestinationPrefix = ... }` produces a
  correspondingly updated `Identity` on `ObservedRoute` /
  `DesiredPrefixRoute` / `DesiredEndpointRoute`. Existing `with` usages vary
  `Metric`, which does not affect identity. **This is the exact invariant
  design B would break**, and it is currently unguarded.
- No test pins the culture-sensitivity of the interpolation.

No permanent tests were added in this analysis phase, as the brief directs.

## 14. Files changed

- `docs/performance/baselines/phase-31.1-route-identity-caching-analysis.md` — NEW
- `docs/performance/README.md` — one link added

Temporary diagnostic
`IranDirect.Core.Tests/Performance/RouteIdentityCachingAnalysisTests.cs` was
created for measurement and **deleted** before completion. No production,
test or benchmark source remains modified.
