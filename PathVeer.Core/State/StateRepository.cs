using PathVeer.Core.Persistence;

namespace PathVeer.Core.State;

public sealed class StateRepository :
    JsonStore<PathVeerState>
{
    public StateRepository(string statePath)
        : base(statePath)
    {
    }
}