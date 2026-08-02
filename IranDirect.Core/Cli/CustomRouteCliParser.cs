namespace IranDirect.Core.Cli;

public static class CustomRouteCliParser
{
    public static CustomRouteCliParseResult Parse(
        IReadOnlyList<string> args)
    {
        string? subcommand =
            args.Count == 0
                ? null
                : args[0].ToLowerInvariant();

        return subcommand switch
        {
            null or "list" =>
                new CustomRouteCliParseResult
                {
                    Command = CustomRouteCliCommand.List
                },

            "resolve" =>
                new CustomRouteCliParseResult
                {
                    Command = CustomRouteCliCommand.Resolve
                },

            "status" =>
                new CustomRouteCliParseResult
                {
                    Command = CustomRouteCliCommand.Status
                },

            "invalidate" =>
                ParseId(
                    args,
                    CustomRouteCliCommand.Invalidate),

            "invalidate-all" =>
                new CustomRouteCliParseResult
                {
                    Command = CustomRouteCliCommand.InvalidateAll
                },

            "add-domain" =>
                ParseAdd(
                    args,
                    CustomRouteCliCommand.AddDomain,
                    "domain"),

            "add-ip" =>
                ParseAdd(
                    args,
                    CustomRouteCliCommand.AddIp,
                    "IPv4 address"),

            "add-cidr" =>
                ParseAdd(
                    args,
                    CustomRouteCliCommand.AddCidr,
                    "CIDR"),

            "enable" =>
                ParseId(args, CustomRouteCliCommand.Enable),

            "disable" =>
                ParseId(args, CustomRouteCliCommand.Disable),

            "remove" =>
                ParseId(args, CustomRouteCliCommand.Remove),

            _ =>
                new CustomRouteCliParseResult
                {
                    Error =
                        $"Unknown custom-routes subcommand " +
                        $"'{subcommand}'."
                }
        };
    }

    private static CustomRouteCliParseResult ParseAdd(
        IReadOnlyList<string> args,
        CustomRouteCliCommand command,
        string label)
    {
        string? value =
            args.Count > 1
                ? args[1]
                : null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return new CustomRouteCliParseResult
            {
                Error = $"A {label} value is required."
            };
        }

        string? description =
            args.Count > 2
                ? args[2]
                : null;

        return new CustomRouteCliParseResult
        {
            Command = command,
            Value = value,
            Description = description
        };
    }

    private static CustomRouteCliParseResult ParseId(
        IReadOnlyList<string> args,
        CustomRouteCliCommand command)
    {
        string? id =
            args.Count > 1
                ? args[1]
                : null;

        if (string.IsNullOrWhiteSpace(id) ||
            !Guid.TryParse(id, out _))
        {
            return new CustomRouteCliParseResult
            {
                Error =
                    "A valid route ID (GUID) is required."
            };
        }

        return new CustomRouteCliParseResult
        {
            Command = command,
            Value = id
        };
    }
}
