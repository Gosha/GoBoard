using System.Diagnostics;

namespace GoBoard.Vr;

// Opt-in bounded aggregates only: no key identities, labels, or input text.
internal sealed class RenderTimings
{
    private sealed class Samples
    {
        public readonly double[] Values = new double[1024];
        public int Count, Stored;
        public double Max;
        public void Add(double value)
        {
            Values[Count++ % Values.Length] = value;
            Stored = Math.Min(Count, Values.Length);
            Max = Math.Max(Max, value);
        }
    }
    internal bool Enabled { get; }
    private readonly Dictionary<string, Samples> samples = new();
    private long lastReport = Stopwatch.GetTimestamp();

    public RenderTimings(bool? enabled = null) => Enabled = enabled ?? Environment.GetEnvironmentVariable("GOBOARD_TRACE_RENDER") == "1";
    public long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public void End(string stage, long start)
    {
        if (Enabled && start != 0) Add(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    public void Add(string stage, double milliseconds)
    {
        if (!Enabled || !double.IsFinite(milliseconds) || milliseconds < 0) return;
        if (!samples.TryGetValue(stage, out var values)) samples.Add(stage, values = new());
        values.Add(milliseconds);
    }
    public void Report(bool force = false)
    {
        if (!Enabled || !force && Stopwatch.GetElapsedTime(lastReport).TotalSeconds < 5) return;
        foreach (var (stage, values) in samples)
        {
            var sorted = values.Values[..values.Stored];
            Array.Sort(sorted);
            double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
            Console.WriteLine(FormattableString.Invariant($"Render timing {stage}: n={values.Count}, sampled={sorted.Length}, p50={Percentile(.5):F3} ms, p95={Percentile(.95):F3} ms, p99={Percentile(.99):F3} ms, max={values.Max:F3} ms"));
        }
        samples.Clear();
        lastReport = Stopwatch.GetTimestamp();
    }
}
