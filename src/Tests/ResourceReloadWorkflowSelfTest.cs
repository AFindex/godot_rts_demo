using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ResourceReloadWorkflowSelfTest
{
    public static SelfTestResult Run()
    {
        var debouncer = new ResourceReloadDebouncer(
            quietPeriodSeconds: 0.25,
            retryPeriodSeconds: 0.20,
            maximumAttempts: 3);
        debouncer.Notify(RuntimeResourceChangeKind.Navigation, 0.00);
        debouncer.Notify(RuntimeResourceChangeKind.ClearanceBake, 0.05);
        debouncer.Notify(RuntimeResourceChangeKind.ClearanceBake, 0.10);
        var prematureRejected = !debouncer.TryBegin(0.34, out _);
        var coalesced = debouncer.TryBegin(0.35, out var first) &&
                        first.Changes ==
                            (RuntimeResourceChangeKind.Navigation |
                             RuntimeResourceChangeKind.ClearanceBake) &&
                        first.NoticeCount == 3 && first.Attempt == 1;
        var retry = debouncer.Complete(
            first, succeeded: false, retryable: true, nowSeconds: 0.35);
        var retryDelayed = !debouncer.TryBegin(0.54, out _);
        var secondReady = debouncer.TryBegin(0.55, out var second) &&
                          second.Attempt == 2;
        var completed = debouncer.Complete(
            second, succeeded: true, retryable: false, nowSeconds: 0.55);

        var terminal = new ResourceReloadDebouncer(
            quietPeriodSeconds: 0d,
            retryPeriodSeconds: 0d,
            maximumAttempts: 2);
        terminal.Notify(RuntimeResourceChangeKind.GameplayProfiles, 1d);
        terminal.TryBegin(1d, out var terminalFirst);
        terminal.Complete(
            terminalFirst, succeeded: false, retryable: true, nowSeconds: 1d);
        terminal.TryBegin(1d, out var terminalSecond);
        var terminalResult = terminal.Complete(
            terminalSecond, succeeded: false, retryable: true, nowSeconds: 1d);

        var passed = prematureRejected && coalesced && retry.WillRetry &&
                     retryDelayed && secondReady && !completed.Pending &&
                     !debouncer.HasPendingChanges &&
                     terminalResult.TerminalFailure &&
                     !terminal.HasPendingChanges;
        return new SelfTestResult(
            passed,
            $"coalesced={first.NoticeCount}, changes={first.Changes}, " +
            $"retry={second.Attempt}, terminal={terminalResult.TerminalFailure}");
    }
}
