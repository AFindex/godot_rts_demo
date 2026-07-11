namespace RtsDemo.Simulation;

public enum ClearanceBakeCommitCode : byte
{
    Success,
    MissingBaseline,
    NavigationMismatch,
    GridLayoutMismatch,
    UnsupportedPathProvider,
    RecordingActive
}

public readonly record struct ClearanceBakeCommitValidation(
    ClearanceBakeCommitCode Code,
    string Message)
{
    public bool Succeeded => Code == ClearanceBakeCommitCode.Success;
}

public readonly record struct ClearanceBakeCommitResult(
    ClearanceBakeCommitCode Code,
    ulong PreviousBakeHash,
    ulong CandidateBakeHash,
    int ReplannedUnits)
{
    public bool Succeeded => Code == ClearanceBakeCommitCode.Success;
}

internal interface IClearanceBakeReloadTarget
{
    ClearanceBakeCommitValidation ValidateClearanceBake(
        ClearanceBakeSnapshot candidate);

    void CommitClearanceBake(ClearanceBakeSnapshot candidate);
}

internal static class ClearanceBakeReloadValidator
{
    public static ClearanceBakeCommitValidation Validate(
        ClearanceBakeSnapshot? current,
        ClearanceBakeSnapshot candidate,
        StaticWorld world,
        float cellSize)
    {
        if (current is null)
        {
            return Failure(
                ClearanceBakeCommitCode.MissingBaseline,
                "A versioned baseline Bake is required for live replacement.");
        }
        if (candidate.SourceNavigationHash != current.SourceNavigationHash)
        {
            return Failure(
                ClearanceBakeCommitCode.NavigationMismatch,
                "Candidate Bake must target the current Navigation hash.");
        }
        if (candidate.WorldBounds != world.Bounds ||
            MathF.Abs(candidate.CellSize - cellSize) > 0.0001f ||
            candidate.Columns != current.Columns ||
            candidate.Rows != current.Rows)
        {
            return Failure(
                ClearanceBakeCommitCode.GridLayoutMismatch,
                "Candidate Bake must preserve world and sampled grid layout.");
        }
        for (var classIndex = 0; classIndex < 3; classIndex++)
        {
            var movementClass = (MovementClass)classIndex;
            if (MathF.Abs(
                    candidate.Layer(movementClass).NavigationRadius -
                    current.Layer(movementClass).NavigationRadius) > 0.0001f)
            {
                return Failure(
                    ClearanceBakeCommitCode.GridLayoutMismatch,
                    "Candidate Bake must preserve Movement Class radii.");
            }
        }
        return new ClearanceBakeCommitValidation(
            ClearanceBakeCommitCode.Success, string.Empty);
    }

    private static ClearanceBakeCommitValidation Failure(
        ClearanceBakeCommitCode code,
        string message) => new(code, message);
}
