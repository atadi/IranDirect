namespace PathVeer.Core.Configuration;

/// <summary>
/// Country-routing support policy.
///
/// Phase 35.2 introduced a temporary transitional gate that only permitted the
/// legacy/default country (IR) until the prefix data source was generalized.
///
/// Phase 35.3 replaced the Iran-only source with a generic country-prefix
/// pipeline driven by <see cref="DesiredConfiguration.DirectCountryCode"/>. The
/// RIPEstat country-resource-list endpoint is global (ISO 3166-1 alpha-2), so a
/// valid ISO country is routable whenever the generic source can provide a
/// valid dataset for it. There is no hand-written whitelist.
///
/// Country validity is therefore enforced by the <see cref="DirectCountryCode"/>
/// value object itself: an unparseable or non-ISO code throws at parse/conversion
/// time, and a legacy-absent (null) configuration code defaults to IR at the
/// configuration layer. The dedicated <c>IsDirectCountrySupported</c> gate that
/// existed during the 35.2 transition was removed in 35.3 because no code path
/// needed it — the source-level contract (a valid fetch requires the selected
/// country's dataset to be obtainable and validated) is enforced where the
/// dataset is acquired, not behind a redundant boolean.
/// </summary>
public static class DirectCountryRouting
{
}
