namespace RtsDemo.Simulation;

[Flags]
public enum RuntimeResourceChangeKind : byte
{
    None = 0,
    Navigation = 1,
    GameplayProfiles = 2,
    ClearanceBake = 4
}

public readonly record struct ResourceReloadBatch(
    int Generation,
    RuntimeResourceChangeKind Changes,
    int NoticeCount,
    int Attempt);

public readonly record struct ResourceReloadCompletion(
    bool Pending,
    bool WillRetry,
    bool TerminalFailure);

/// <summary>
/// Engine-independent coalescing and retry policy for resource file changes.
/// File-system callbacks only enqueue notices; callers execute loading on their
/// owning thread after TryBegin returns a ready batch.
/// </summary>
public sealed class ResourceReloadDebouncer
{
    private readonly double _quietPeriodSeconds;
    private readonly double _retryPeriodSeconds;
    private readonly int _maximumAttempts;
    private RuntimeResourceChangeKind _changes;
    private int _noticeCount;
    private int _generation;
    private int _attempt;
    private double _nextEligibleTime;
    private bool _inFlight;

    public ResourceReloadDebouncer(
        double quietPeriodSeconds = 0.25,
        double retryPeriodSeconds = 0.25,
        int maximumAttempts = 5)
    {
        if (!double.IsFinite(quietPeriodSeconds) || quietPeriodSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriodSeconds));
        }
        if (!double.IsFinite(retryPeriodSeconds) || retryPeriodSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(retryPeriodSeconds));
        }
        if (maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }
        _quietPeriodSeconds = quietPeriodSeconds;
        _retryPeriodSeconds = retryPeriodSeconds;
        _maximumAttempts = maximumAttempts;
    }

    public RuntimeResourceChangeKind PendingChanges => _changes;
    public int PendingNoticeCount => _noticeCount;
    public bool HasPendingChanges => _changes != RuntimeResourceChangeKind.None;

    public void Notify(RuntimeResourceChangeKind changes, double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (changes == RuntimeResourceChangeKind.None ||
            (changes & ~(RuntimeResourceChangeKind.Navigation |
                         RuntimeResourceChangeKind.GameplayProfiles |
                         RuntimeResourceChangeKind.ClearanceBake)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changes));
        }

        _changes |= changes;
        _noticeCount++;
        _generation++;
        _attempt = 0;
        _nextEligibleTime = nowSeconds + _quietPeriodSeconds;
    }

    public bool TryBegin(double nowSeconds, out ResourceReloadBatch batch)
    {
        ValidateTime(nowSeconds);
        if (_inFlight || !HasPendingChanges || nowSeconds < _nextEligibleTime)
        {
            batch = default;
            return false;
        }

        _attempt++;
        _inFlight = true;
        batch = new ResourceReloadBatch(
            _generation, _changes, _noticeCount, _attempt);
        return true;
    }

    public ResourceReloadCompletion Complete(
        ResourceReloadBatch batch,
        bool succeeded,
        bool retryable,
        double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (!_inFlight)
        {
            throw new InvalidOperationException("No resource reload batch is active.");
        }
        _inFlight = false;

        if (batch.Generation != _generation)
        {
            return new ResourceReloadCompletion(true, false, false);
        }
        if (succeeded)
        {
            ClearPending();
            return new ResourceReloadCompletion(false, false, false);
        }
        if (retryable && _attempt < _maximumAttempts)
        {
            _nextEligibleTime = nowSeconds + _retryPeriodSeconds;
            return new ResourceReloadCompletion(true, true, false);
        }

        ClearPending();
        return new ResourceReloadCompletion(false, false, true);
    }

    private void ClearPending()
    {
        _changes = RuntimeResourceChangeKind.None;
        _noticeCount = 0;
        _attempt = 0;
        _nextEligibleTime = 0d;
    }

    private static void ValidateTime(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
    }
}

public enum ResourceReloadWorkflowState : byte
{
    Idle,
    Debouncing,
    Loading,
    RetryScheduled,
    Unchanged,
    Applied,
    RebuildRequired,
    Failed
}

public readonly record struct ResourceReloadWorkflowSnapshot(
    ResourceReloadWorkflowState State,
    RuntimeResourceChangeKind Changes,
    int NoticeCount,
    int Attempt,
    ResourceReloadImpact Impact,
    ClearanceBakeCommitCode? CommitCode,
    int ReplannedUnits,
    string Message)
{
    public static ResourceReloadWorkflowSnapshot Idle => new(
        ResourceReloadWorkflowState.Idle,
        RuntimeResourceChangeKind.None,
        0,
        0,
        ResourceReloadImpact.None,
        null,
        0,
        "Watching Navigation / Profiles / Clearance Bake");
}
