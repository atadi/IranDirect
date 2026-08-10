# PathVeer — RIPEstat Terms Review (Release-Closure Blocker H)

Factual, engineering/business review of PathVeer's use of RIPE NCC RIPEstat,
based on the repository's actual consumption and the authoritative RIPE NCC
terms of use. This is NOT formal legal advice; it is an engineering readiness
assessment that surfaces the exact business/legal decision required.

## 1. What PathVeer actually consumes

- Source: `OfficialCountryPrefixSource` implements `ICountryPrefixSource`
  (see `docs/globalization/phase-35.3-country-prefix-source.md`). It pulls
  per-country IPv4 prefixes from **RIPEstat** (RIPE NCC's data API) and persists
  them via `CountryPrefixStore` (per-country cache + metadata + history).
- Use pattern: prefix data is fetched (refresh) and cached locally per country;
  the engine is geography-neutral and fails closed when prefix data is
  unavailable. Live runtime use is a refresh, not a hard dependency — offline
  operation uses the cached snapshot.
- This is **data consumption** (country → IPv4 prefix set), not the RIPEstat
  *services* (no embedding of RIPEstat UI/branding).

## 2. Authoritative RIPE NCC terms (Article 3 — Use of Data)

- RIPE NCC's database/raw data and RIPEstat **data** are free to use, including
  internally and for non-commercial purposes.
- **Commercial use** is restricted. Article 3 requires **written permission
  from RIPE NCC** when the data is used to:
  1. provide a paid service or product, or a product/service that is a
     derivative based (in whole or part) on RIPE NCC data; or
  2. package RIPE NCC data together with data from other sources as a
     commercial product.
- **Attribution** is expected (acknowledge RIPE NCC / RIPEstat as the source).
- There is no cost for the data itself; permission is a licensing/approval step,
  not a fee in the ordinary case.

## 3. Does PathVeer's use trigger the commercial-use clause?

Plausibly YES, and that must be dispositioned rather than assumed:
- PathVeer is a **commercial product** (Windows v1 paid/public release).
- It embeds RIPEstat-derived country prefix data into its routing function,
  which is a core part of the shipped product.
- This reads as "a product that is a derivative based (in part) on RIPE NCC
  data" → **written permission from RIPE NCC is the safe, correct requirement.**

The alternative reading (data is free, only the *services* are restricted) is
not safe to rely on for a commercial release without RIPE NCC confirmation.

## 4. Engineering status (already satisfied)

- Prefix fetch fails closed on unavailability (`PrefixSource*FaultInjection`
  tests; per-country cache). No runtime hard dependency on RIPEstat.
- Data is cached per country; a publish-time snapshot can reduce live-API
  dependence (see §5).
- No code change is required for release closure — this is a licensing
  disposition, already tracked as a pre-launch task in phase-35.7 / phase-36.8
  acceptance ("RIPEstat pre-launch terms review").

## 5. Recommended business disposition (for user decision)

In dependency order, lowest-friction first:

1. **Request written permission from RIPE NCC** for commercial use of RIPEstat
   country-prefix data in PathVeer. RIPE NCC has a known process for this and
   typically grants it with attribution. This is the cleanest path and removes
   the blocker entirely. (Recommended default.)
2. **Snapshot-at-publish + reduced live dependency**: generate the country
   prefix set at release-publish time, ship it as a local artifact, and treat
   RIPEstat as a periodic refresh source. This does not remove the
   commercial-use question but narrows live dependence and makes the data
   supply auditable. Engineering already supports per-country caching; a
   publish-time snapshot generator is a small addition if desired.
3. **Substitute source**: if RIPE NCC permission is declined, switch
   `OfficialCountryPrefixSource` to an equivalently-licensed source (e.g., other
   RIR stats APIs or a commercial GeoIP/prefix feed). This is a code change and
   should be evaluated only if permission is refused.

## 6. Required user action

This is a **business/legal risk acceptance** decision (blocker H). The builder
cannot obtain RIPE NCC written permission on the user's behalf. Required:

- Decide: **(a)** authorize the builder to draft/submit a RIPE NCC commercial-use
  permission request (user still owns the legal relationship), **(b)** accept
  the attribution + permission obligation as a release prerequisite, or
  **(c)** choose a substitute prefix source.

Until (a)/(b)/(c) is confirmed, blocker H remains OPEN. Engineering is complete;
the gate is a licensing disposition, not a code defect.
