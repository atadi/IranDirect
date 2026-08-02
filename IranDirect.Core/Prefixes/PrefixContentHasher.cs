using System.Security.Cryptography;
using System.Text;

namespace IranDirect.Core.Prefixes;

public static class PrefixContentHasher
{
    public static string ComputeHash(
        IEnumerable<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        string canonical = Canonicalize(prefixes);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexStringLower(hash);
    }

    public static string Canonicalize(
        IEnumerable<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        return string.Join(
                   "\n",
                   prefixes
                       .Select(prefix => prefix.Trim())
                       .Where(prefix => prefix.Length > 0)
                       .Distinct(StringComparer.Ordinal)
                       .OrderBy(prefix => prefix, StringComparer.Ordinal))
               + "\n";
    }
}
