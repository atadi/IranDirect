namespace IranDirect.Core.Configuration;

/// <summary>
/// Temporary transitional gate for Phase 35.2.
///
/// The prefix data source is still Iran-only until Phase 35.3 generalizes it.
/// A configuration may now carry an explicit non-IR <see
/// cref="DirectCountryCode"/> (e.g. IQ/RO), which is valid and representable,
/// but the runtime must NOT route through the Iran-only source as if that
/// country's prefixes belonged to it.
///
/// Until Phase 35.3, only the legacy/default country (IR, including a null
/// legacy value) is supported. Any other recognized country is valid
/// configuration yet intentionally unsupported for routing, so reconciliation
/// is skipped safely (no route mutation) and the host stays alive.
///
/// REMOVAL POINT (Phase 35.3): replace the body of
/// <see cref="IsDirectCountrySupported"/> with a real check against the
/// generalized country-prefix source (e.g. is a source configured/available for
/// this code?). Once the source supports the selected country, the gate opens
/// and normal reconciliation resumes without further code changes here.
/// </summary>
public static class DirectCountryRouting
{
    public static bool IsDirectCountrySupported(
        DirectCountryCode? code) =>
        code is null || code == DirectCountryCode.IR;
}
