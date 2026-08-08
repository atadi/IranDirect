using PathVeer.Core.Cli;
using PathVeer.Core.Ipc;

namespace PathVeer.Cli;

public static class CustomRouteCliRunner
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitTimeout = 3;
    private const int ExitCanceled = 4;
    private const int ExitUsage = 6;

    public static async Task<int> RunAsync(
        string[] args,
        ICustomRouteCommandSender sender,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        CustomRouteCliParseResult parsed =
            CustomRouteCliParser.Parse(args);

        if (!parsed.IsValid)
        {
            stderr.WriteLine($"Error: {parsed.Error}");
            stderr.WriteLine(
                "Usage: PathVeer.Cli custom-routes " +
                "[list|add-domain <domain> [description]|" +
                "add-ip <ipv4> [description]|" +
                "add-cidr <cidr> [description]|" +
                "enable <id>|disable <id>|remove <id>|resolve|" +
                "status|invalidate <id>|invalidate-all]");
            return ExitUsage;
        }

        try
        {
            ServiceResponse response =
                await sender.SendAsync(
                    MapCommand(parsed.Command!.Value),
                    parsed.Value,
                    parsed.Description);

            if (!response.Success)
            {
                string prefix =
                    string.IsNullOrWhiteSpace(response.ErrorCode)
                        ? ""
                        : $"[{response.ErrorCode}] ";

                stderr.WriteLine(prefix + response.Message);
                return ExitFailure;
            }

            switch (parsed.Command.Value)
            {
                case CustomRouteCliCommand.List:
                    foreach (string line in
                             CustomRouteCliRenderer.RenderList(
                                 response.CustomRoutes))
                    {
                        stdout.WriteLine(line);
                    }

                    break;

                case CustomRouteCliCommand.Resolve:
                    foreach (string line in
                             CustomRouteCliRenderer.RenderResolve(
                                 response.CustomRouteResolution!))
                    {
                        stdout.WriteLine(line);
                    }

                    break;

                case CustomRouteCliCommand.Status:
                    foreach (string line in
                             CustomRouteCliRenderer.RenderCacheStatus(
                                 response.CustomRouteDnsCacheStatuses))
                    {
                        stdout.WriteLine(line);
                    }

                    break;

                default:
                    stdout.WriteLine(response.Message);
                    break;
            }

            return ExitSuccess;
        }
        catch (TimeoutException exception)
        {
            stderr.WriteLine(exception.Message);
            return ExitTimeout;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("The operation was canceled.");
            return ExitCanceled;
        }
        catch (Exception exception)
        {
            stderr.WriteLine(
                $"PathVeer command failed: " +
                $"{exception.Message}");
            return ExitFailure;
        }
    }

    private static PathVeerCommand MapCommand(
        CustomRouteCliCommand command)
    {
        return command switch
        {
            CustomRouteCliCommand.List =>
                PathVeerCommand.CustomRoutesList,
            CustomRouteCliCommand.AddDomain =>
                PathVeerCommand.CustomRoutesAddDomain,
            CustomRouteCliCommand.AddIp =>
                PathVeerCommand.CustomRoutesAddIp,
            CustomRouteCliCommand.AddCidr =>
                PathVeerCommand.CustomRoutesAddCidr,
            CustomRouteCliCommand.Enable =>
                PathVeerCommand.CustomRoutesEnable,
            CustomRouteCliCommand.Disable =>
                PathVeerCommand.CustomRoutesDisable,
            CustomRouteCliCommand.Remove =>
                PathVeerCommand.CustomRoutesRemove,
            CustomRouteCliCommand.Resolve =>
                PathVeerCommand.CustomRoutesResolve,
            CustomRouteCliCommand.Status =>
                PathVeerCommand.CustomRoutesCacheStatus,
            CustomRouteCliCommand.Invalidate =>
                PathVeerCommand.CustomRoutesInvalidateCache,
            CustomRouteCliCommand.InvalidateAll =>
                PathVeerCommand.CustomRoutesInvalidateAllCaches,
            _ => throw new ArgumentOutOfRangeException(
                nameof(command))
        };
    }
}
