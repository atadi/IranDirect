namespace IranDirect.Core.Testing.FaultInjection;

public static class FaultInjectionResolver
{
    public static bool ShouldFail(
        IFaultInjectionPolicy? injectedPolicy,
        FaultInjectionPoint point)
    {
        if (FaultInjectionScope.IsActive)
        {
            return FaultInjectionScope.ShouldFail(point);
        }

        return (injectedPolicy ?? FaultInjectionPolicy.Never)
            .ShouldFail(point);
    }
}
