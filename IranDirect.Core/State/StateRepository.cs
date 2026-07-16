using IranDirect.Core.Persistence;

namespace IranDirect.Core.State;

public sealed class StateRepository :
    JsonStore<IranDirectState>
{
    public StateRepository(string statePath)
        : base(statePath)
    {
    }
}