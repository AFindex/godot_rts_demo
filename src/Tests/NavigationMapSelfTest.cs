using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class NavigationMapSelfTest
{
    public static SelfTestResult Run()
    {
        var canonical = DemoMapDefinition.CreateSnapshot();
        if (!NavigationMapSnapshot.TryCreate(
                canonical.FormatVersion,
                canonical.WorldBounds,
                canonical.Obstacles,
                canonical.PortalNodes,
                canonical.PortalEdges,
                canonical.Chokes,
                out var repeated,
                out var repeatedValidation) ||
            repeated is null || !repeatedValidation.IsValid ||
            canonical.StableHash == 0UL || canonical.StableHash != repeated.StableHash ||
            !canonical.CanonicalBytes.Span.SequenceEqual(repeated.CanonicalBytes.Span))
        {
            return new SelfTestResult(false, "canonical bytes changed for identical input");
        }

        var changedEdges = canonical.PortalEdges.ToArray();
        changedEdges[0] = changedEdges[0] with { Width = changedEdges[0].Width + 1f };
        if (!NavigationMapSnapshot.TryCreate(
                canonical.FormatVersion,
                canonical.WorldBounds,
                canonical.Obstacles,
                canonical.PortalNodes,
                changedEdges,
                canonical.Chokes,
                out var changed,
                out var changedValidation) ||
            changed is null || !changedValidation.IsValid ||
            changed.StableHash == canonical.StableHash)
        {
            return new SelfTestResult(false, "stable hash did not detect changed edge width");
        }

        var invalidEdges = canonical.PortalEdges.ToArray();
        invalidEdges[0] = invalidEdges[0] with { ToNode = 99 };
        var invalidAccepted = NavigationMapSnapshot.TryCreate(
            canonical.FormatVersion,
            canonical.WorldBounds,
            canonical.Obstacles,
            canonical.PortalNodes,
            invalidEdges,
            canonical.Chokes,
            out _,
            out var invalidValidation);
        if (invalidAccepted ||
            invalidValidation.FirstError != NavigationMapErrorCode.InvalidPortalEdge)
        {
            return new SelfTestResult(
                false,
                $"invalid edge error={invalidValidation.FirstError}");
        }

        return new SelfTestResult(
            true,
            $"format={canonical.FormatVersion}, hash={canonical.StableHashText}, " +
            $"bytes={canonical.CanonicalBytes.Length}, " +
            $"invalidEdge={invalidValidation.FirstError}");
    }
}
