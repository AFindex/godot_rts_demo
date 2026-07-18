using System.Threading;

namespace War3Rts;

/// <summary>
/// One-shot scene launch options used by the in-process front end. Command
/// line automation remains authoritative when the War3 scene is started as a
/// separate process; this bridge only exists because ChangeSceneToFile cannot
/// add process arguments.
/// </summary>
public static class War3LaunchRequest
{
    public const string InteractiveStressArgument =
        "--war3-interactive-stress";

    private static string[]? _pendingArguments;

    private static readonly string[] InteractiveStressArguments =
    [
        InteractiveStressArgument,
        War3AutomatedSkirmishStressMap.EnableArgument,
        "--war3-auto-skirmish-map-columns=320",
        "--war3-auto-skirmish-map-rows=160",
        "--war3-auto-skirmish-ticks-per-frame=1",
        "--war3-runtime-profile",
        "--war3-profile-variant=interactive-rendered-800",
        "--war3-profile-warmup=5",
        "--war3-profile-seconds=90",
        "--war3-profile-spike-ms=25",
        "--war3-profile-no-quit",
        "--war3-stress-test",
        "--war3-stress-units-per-team=400",
        "--war3-stress-army-inset=1050",
        "--war3-stress-quality-report=300",
        "--war3-stress-builders=48",
        "--war3-stress-build-slots=96",
        "--war3-stress-build-interval=6",
        "--war3-stress-building-lifetime=300",
        "--war3-stress-combat-refresh=120",
        "--war3-stress-respawn=60"
    ];

    public static void RequestInteractiveStress() =>
        Interlocked.Exchange(
            ref _pendingArguments,
            InteractiveStressArguments.ToArray());

    public static void Clear() =>
        Interlocked.Exchange(ref _pendingArguments, null);

    public static string[] ConsumeArguments(string[] processArguments)
    {
        var pending = Interlocked.Exchange(ref _pendingArguments, null);
        return pending is null
            ? processArguments
            : [.. pending, .. processArguments];
    }
}
