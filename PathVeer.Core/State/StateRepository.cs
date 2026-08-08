using PathVeer.Core.Persistence;

namespace PathVeer.Core.State;

public sealed class StateRepository :
    JsonStore<IranDirectState>
{
    public StateRepository(string statePath)
        : base(statePath)
    {
    }
}