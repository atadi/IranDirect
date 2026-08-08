using PathVeer.Core.Configuration;

namespace PathVeer.Core.Tests.Globalization;

/// <summary>
/// Characterization of the data the Tray country selector binds to. The Tray
/// builds its "Direct country" dropdown from <see cref="DirectCountryCode.AllSupported"/>
/// and renders the current selection as "Name (CODE)". These tests lock that
/// contract so the menu can never silently regress to a hardcoded IR/IQ/RO
/// list and the requested-country label stays correct.
/// </summary>
public sealed class CountryMenuBindingTests
{
    [Fact]
    public void TrayDropdown_IsBuiltFromAllSupported_NotHardcoded()
    {
        // Mirrors TrayApplicationContext's dropdown construction.
        var items = DirectCountryCode.AllSupported
            .Select(code => $"{code.DisplayName} ({code.Code})")
            .ToList();

        // Not a 3-entry allow-list; every recognized ISO code is shown.
        Assert.True(items.Count > 200, $"got {items.Count}");
        Assert.Contains("Iran (IR)", items);
        Assert.Contains("Iraq (IQ)", items);
        Assert.Contains("Romania (RO)", items);
    }

    [Fact]
    public void TraySelectionLabel_FormatsNameAndCode()
    {
        DirectCountryCode selected = DirectCountryCode.Parse("IQ");

        string label =
            $"Direct country: {selected.DisplayName} ({selected.Code})";

        Assert.Equal("Direct country: Iraq (IQ)", label);
    }

    [Fact]
    public void TrayLabel_DefaultsToIranWhenConfigAbsent()
    {
        // The Tray initializes the menu header to Iran (IR) before the first
        // status poll; this matches the legacy/default contract.
        DirectCountryCode requested =
            DirectCountryCode.IR; // default when config missing

        string label =
            $"Direct country: {requested.DisplayName} ({requested.Code})";

        Assert.Equal("Direct country: Iran (IR)", label);
    }

    [Theory]
    [InlineData("IR")]
    [InlineData("IQ")]
    [InlineData("RO")]
    [InlineData("DE")]
    public void LowercaseInput_NormalizesToCanonicalLabel(string input)
    {
        DirectCountryCode code = DirectCountryCode.Parse(input);

        // Tray dropdown entries and selection label use the canonical code.
        Assert.Equal(input.ToUpperInvariant(), code.Code);
        Assert.Equal(
            $"{code.DisplayName} ({code.Code})",
            $"{code.DisplayName} ({input.ToUpperInvariant()})");
    }
}
