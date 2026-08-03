namespace IranDirect.Core.Testing.FaultInjection;

public interface IFaultInjectionPolicy
{
    bool ShouldFail(FaultInjectionPoint point);
}
