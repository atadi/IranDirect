namespace PathVeer.Core.Vpn;

public sealed class OpenVpnProfileParser
{
    public async Task<IReadOnlyList<VpnEndpointDefinition>>
        ParseAsync(
            string profilePath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            profilePath);

        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException(
                "The configured OpenVPN profile was not found.",
                profilePath);
        }

        string[] lines = await File.ReadAllLinesAsync(
            profilePath,
            cancellationToken);

        string defaultProtocol = "udp";

        foreach (string line in lines)
        {
            string[] tokens = Tokenize(line);

            if (tokens.Length >= 2 &&
                tokens[0].Equals(
                    "proto",
                    StringComparison.OrdinalIgnoreCase))
            {
                defaultProtocol =
                    NormalizeProtocol(tokens[1]);
            }
        }

        List<VpnEndpointDefinition> endpoints = [];

        foreach (string line in lines)
        {
            string[] tokens = Tokenize(line);

            if (tokens.Length < 2 ||
                !tokens[0].Equals(
                    "remote",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string host = tokens[1];

            int port = 1194;

            if (tokens.Length >= 3 &&
                !int.TryParse(tokens[2], out port))
            {
                throw new InvalidOperationException(
                    $"Invalid OpenVPN remote port: {tokens[2]}");
            }

            string protocol =
                tokens.Length >= 4
                    ? NormalizeProtocol(tokens[3])
                    : defaultProtocol;

            endpoints.Add(
                new VpnEndpointDefinition
                {
                    Host = host,
                    Port = port,
                    Protocol = protocol
                });
        }

        if (endpoints.Count == 0)
        {
            throw new InvalidOperationException(
                "The OpenVPN profile contains no remote directives.");
        }

        return endpoints;
    }

    private static string[] Tokenize(string line)
    {
        string value = line.Trim();

        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith('#') ||
            value.StartsWith(';'))
        {
            return [];
        }

        return value.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
    }

    private static string NormalizeProtocol(
        string protocol)
    {
        string value =
            protocol.Trim().ToLowerInvariant();

        return value switch
        {
            "udp" or "udp4" => "udp",
            "tcp" or "tcp4" or
            "tcp-client" => "tcp",
            _ => value
        };
    }
}