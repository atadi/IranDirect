namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Captures <see cref="ResourceSample"/> values at baseline, at
/// sampling boundaries (each cycle-like step) when the configured
/// interval is due, and at the end of a run.
/// </summary>
public sealed class ServiceSimulationSampler
{
    private readonly SimulatedRuntimeEnvironment _environment;
    private readonly ServiceSimulationRunOptions _options;
    private readonly object _gate = new();
    private readonly List<ResourceSample> _samples = [];
    private int _boundaryCount;

    public ServiceSimulationSampler(
        SimulatedRuntimeEnvironment environment,
        ServiceSimulationRunOptions options)
    {
        _environment = environment;
        _options = options;
    }

    public IReadOnlyList<ResourceSample> Samples
    {
        get
        {
            lock (_gate)
            {
                return _samples.ToArray();
            }
        }
    }

    public async Task CaptureBaselineAsync(
        CancellationToken cancellationToken = default)
    {
        ResourceSample sample = await ResourceSample.CaptureAsync(
            _environment,
            cycleIndex: 0,
            cancellationToken);

        lock (_gate)
        {
            _samples.Clear();
            _samples.Add(sample);
        }
    }

    /// <summary>
    /// Increments the boundary counter and captures a sample when the
    /// configured interval is due.
    /// </summary>
    public async Task MaybeSampleBoundaryAsync(
        CancellationToken cancellationToken = default)
    {
        int boundary = Interlocked.Increment(ref _boundaryCount);

        if (_options.SampleEvery <= 0
            || boundary % _options.SampleEvery != 0)
        {
            return;
        }

        ResourceSample sample = await ResourceSample.CaptureAsync(
            _environment,
            boundary,
            cancellationToken);

        lock (_gate)
        {
            _samples.Add(sample);
        }
    }

    public async Task CaptureFinalAsync(
        CancellationToken cancellationToken = default)
    {
        ResourceSample sample = await ResourceSample.CaptureAsync(
            _environment,
            Math.Max(_boundaryCount, 1),
            cancellationToken);

        lock (_gate)
        {
            _samples.Add(sample);
        }
    }
}
