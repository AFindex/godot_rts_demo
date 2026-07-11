using System.Collections.Concurrent;
using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

/// <summary>
/// Thin Godot composition service for the resource reload workflow. File
/// callbacks never invoke Godot APIs; fresh loading and simulation commits run
/// from _Process on the main thread.
/// </summary>
public partial class RuntimeResourceFileWatcher : Node
{
    private readonly ConcurrentQueue<RuntimeResourceChangeKind> _notices = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private ResourceReloadDebouncer _debouncer = new();
    private RuntimeResourceSetSnapshot? _current;
    private RtsSimulation? _simulation;
    private string _navigationPath = string.Empty;
    private string _profilesPath = string.Empty;
    private string _bakePath = string.Empty;
    private double _elapsedSeconds;

    public ResourceReloadWorkflowSnapshot Status { get; private set; } =
        ResourceReloadWorkflowSnapshot.Idle;

    public event Action<ResourceReloadWorkflowSnapshot>? StatusChanged;
    public event Action<RuntimeResourceSetSnapshot>? ResourceSetApplied;

    public void Start(
        string navigationPath,
        string profilesPath,
        string bakePath,
        RuntimeResourceSetSnapshot current,
        RtsSimulation simulation,
        bool enableFileSystemWatchers = true)
    {
        Stop();
        _navigationPath = navigationPath;
        _profilesPath = profilesPath;
        _bakePath = bakePath;
        _current = current;
        _simulation = simulation;
        _debouncer = new ResourceReloadDebouncer();
        _elapsedSeconds = 0d;
        Publish(ResourceReloadWorkflowSnapshot.Idle);
        if (enableFileSystemWatchers)
        {
            AddWatcher(navigationPath, RuntimeResourceChangeKind.Navigation);
            AddWatcher(profilesPath, RuntimeResourceChangeKind.GameplayProfiles);
            AddWatcher(bakePath, RuntimeResourceChangeKind.ClearanceBake);
        }
    }

    public void NotifyChanged(RuntimeResourceChangeKind kind) =>
        _notices.Enqueue(kind);

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        while (_notices.TryDequeue(out _))
        {
        }
        _current = null;
        _simulation = null;
    }

    public override void _ExitTree() => Stop();

    public override void _Process(double delta)
    {
        if (_current is null || _simulation is null)
        {
            return;
        }
        _elapsedSeconds += Math.Max(0d, delta);
        while (_notices.TryDequeue(out var notice))
        {
            _debouncer.Notify(notice, _elapsedSeconds);
        }
        if (_debouncer.HasPendingChanges &&
            Status.State is not (ResourceReloadWorkflowState.Debouncing or
                ResourceReloadWorkflowState.RetryScheduled))
        {
            Publish(new ResourceReloadWorkflowSnapshot(
                ResourceReloadWorkflowState.Debouncing,
                _debouncer.PendingChanges,
                _debouncer.PendingNoticeCount,
                0,
                ResourceReloadImpact.None,
                null,
                0,
                "Waiting for the resource write burst to settle"));
        }
        if (!_debouncer.TryBegin(_elapsedSeconds, out var batch))
        {
            return;
        }

        Publish(new ResourceReloadWorkflowSnapshot(
            ResourceReloadWorkflowState.Loading,
            batch.Changes,
            batch.NoticeCount,
            batch.Attempt,
            ResourceReloadImpact.None,
            null,
            0,
            "Fresh-loading the complete resource set"));
        ProcessBatch(batch);
    }

    private void ProcessBatch(ResourceReloadBatch batch)
    {
        if (_current is null || _simulation is null)
        {
            return;
        }
        if (!RuntimeResourceSetLoader.TryLoadFresh(
                _navigationPath,
                _profilesPath,
                _bakePath,
                out var candidate,
                out var loadResult) ||
            candidate is null)
        {
            var completion = _debouncer.Complete(
                batch,
                succeeded: false,
                retryable: true,
                nowSeconds: _elapsedSeconds);
            Publish(new ResourceReloadWorkflowSnapshot(
                completion.WillRetry
                    ? ResourceReloadWorkflowState.RetryScheduled
                    : ResourceReloadWorkflowState.Failed,
                batch.Changes,
                batch.NoticeCount,
                batch.Attempt,
                ResourceReloadImpact.None,
                null,
                0,
                $"{loadResult.Code}: {loadResult.Message}"));
            return;
        }

        var plan = RuntimeResourceReloadPlan.Create(_current, candidate);
        if (plan.Impact == ResourceReloadImpact.RebuildSimulation)
        {
            _debouncer.Complete(
                batch,
                succeeded: true,
                retryable: false,
                nowSeconds: _elapsedSeconds);
            Publish(new ResourceReloadWorkflowSnapshot(
                ResourceReloadWorkflowState.RebuildRequired,
                batch.Changes,
                batch.NoticeCount,
                batch.Attempt,
                plan.Impact,
                null,
                0,
                "Candidate validated; explicit simulation rebuild required"));
            return;
        }
        if (plan.Impact == ResourceReloadImpact.None)
        {
            _current = candidate;
            _debouncer.Complete(
                batch,
                succeeded: true,
                retryable: false,
                nowSeconds: _elapsedSeconds);
            Publish(new ResourceReloadWorkflowSnapshot(
                ResourceReloadWorkflowState.Unchanged,
                batch.Changes,
                batch.NoticeCount,
                batch.Attempt,
                plan.Impact,
                null,
                0,
                "Fresh load matched the active resource set"));
            return;
        }

        var commit = _simulation.TryCommitClearanceBake(candidate.ClearanceBake);
        _debouncer.Complete(
            batch,
            succeeded: true,
            retryable: false,
            nowSeconds: _elapsedSeconds);
        if (!commit.Succeeded)
        {
            Publish(new ResourceReloadWorkflowSnapshot(
                ResourceReloadWorkflowState.Failed,
                batch.Changes,
                batch.NoticeCount,
                batch.Attempt,
                plan.Impact,
                commit.Code,
                0,
                $"Bake commit rejected: {commit.Code}"));
            return;
        }

        _current = candidate;
        ResourceSetApplied?.Invoke(candidate);
        Publish(new ResourceReloadWorkflowSnapshot(
            ResourceReloadWorkflowState.Applied,
            batch.Changes,
            batch.NoticeCount,
            batch.Attempt,
            plan.Impact,
            commit.Code,
            commit.ReplannedUnits,
            $"Bake-only update committed; replanned {commit.ReplannedUnits} units"));
    }

    private void AddWatcher(string resourcePath, RuntimeResourceChangeKind kind)
    {
        var absolutePath = ProjectSettings.GlobalizePath(resourcePath);
        var directory = Path.GetDirectoryName(absolutePath);
        var fileName = Path.GetFileName(absolutePath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException(
                $"Resource path cannot be watched: {resourcePath}",
                nameof(resourcePath));
        }
        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime | NotifyFilters.Size
        };
        watcher.Changed += (_, _) => _notices.Enqueue(kind);
        watcher.Created += (_, _) => _notices.Enqueue(kind);
        watcher.Deleted += (_, _) => _notices.Enqueue(kind);
        watcher.Renamed += (_, _) => _notices.Enqueue(kind);
        watcher.Error += (_, _) => _notices.Enqueue(kind);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void Publish(ResourceReloadWorkflowSnapshot snapshot)
    {
        Status = snapshot;
        StatusChanged?.Invoke(snapshot);
        GD.Print(
            $"RTS_RESOURCE_WATCH state={snapshot.State} " +
            $"changes={snapshot.Changes} notices={snapshot.NoticeCount} " +
            $"attempt={snapshot.Attempt} impact={snapshot.Impact} " +
            $"commit={snapshot.CommitCode} replanned={snapshot.ReplannedUnits} " +
            $"message={snapshot.Message}");
    }
}
