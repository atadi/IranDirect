using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Testing.FaultInjection;

public sealed class FaultInjectionExceptionTests
{
    [Fact]
    public void DefaultMessage_ContainsPoint()
    {
        FaultInjectionException exception =
            new(FaultInjectionPoint.RouteDelete);

        Assert.Contains(
            nameof(FaultInjectionPoint.RouteDelete),
            exception.Message);
    }

    [Fact]
    public void CustomMessage_IsPreserved()
    {
        const string customMessage = "custom failure";

        FaultInjectionException exception =
            new(FaultInjectionPoint.FileWrite, customMessage);

        Assert.Equal(customMessage, exception.Message);
    }

    [Fact]
    public void PointProperty_IsExposed()
    {
        FaultInjectionException exception =
            new(FaultInjectionPoint.SnapshotCapture);

        Assert.Equal(
            FaultInjectionPoint.SnapshotCapture,
            exception.Point);
    }

    [Fact]
    public void InnerException_IsPreserved()
    {
        InvalidOperationException inner = new("inner cause");

        FaultInjectionException exception =
            new(
                FaultInjectionPoint.HttpRequest,
                "outer message",
                inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Equal("outer message", exception.Message);
    }
}
