namespace IranDirect.Core.Configuration;

/// <summary>
/// Thrown when a string cannot be interpreted as a recognized ISO 3166-1
/// alpha-2 country code.
/// </summary>
public sealed class CountryCodeFormatException : FormatException
{
    public CountryCodeFormatException(string message)
        : base(message)
    {
    }
}
