namespace IranDirect.Core.Testing.FaultInjection;

public class FaultInjectionException : Exception
{
    private const string DefaultMessage = "Fault injected at {0}.";

    public FaultInjectionException(FaultInjectionPoint point)
        : base(string.Format(DefaultMessage, point))
    {
        Point = point;
    }

    public FaultInjectionException(
        FaultInjectionPoint point,
        string? message)
        : base(message)
    {
        Point = point;
    }

    public FaultInjectionException(
        FaultInjectionPoint point,
        string? message,
        Exception? innerException)
        : base(message, innerException)
    {
        Point = point;
    }

    public FaultInjectionPoint Point { get; }
}
