using System.Security.Cryptography;
using System.Text;

namespace iBarter.Routing;

/// <summary>Opt-in audit counters; not consulted by candidate generation or ranking.</summary>
public sealed class TaggedSearchDiagnostics {
    private readonly HashSet<string> expandedFingerprints = new(StringComparer.Ordinal);
    public int Passes { get; internal set; }
    public int StateLimitRestarts { get; internal set; }
    public int FrontierTrims { get; internal set; }
    public long DiscardedQueuedStates { get; internal set; }
    public long ExpandedStates { get; private set; }
    public int DistinctExpandedStates => expandedFingerprints.Count;
    public long ExpandedWithAltCargo { get; private set; }
    public long ExpandedWithElephantCargo { get; private set; }
    public long CompleteCandidates { get; internal set; }
    public long BetterCandidates { get; internal set; }
    public long VoyageCandidates { get; internal set; }

    internal void Observe(string stateKey, TaggedTransportState state) {
        ExpandedStates++;
        expandedFingerprints.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateKey))));
        if (state.Cargo["alt"].Any(i => i.Value > 0)) ExpandedWithAltCargo++;
        if (state.Cargo.Any(c => c.Key.EndsWith("-elephant", StringComparison.Ordinal) && c.Value.Any(i => i.Value > 0))) ExpandedWithElephantCargo++;
    }
}
