using War3Rts;

namespace RtsDemo.Tests;

public static class War3LaunchRequestSelfTest
{
    public static SelfTestResult Run()
    {
        const string processMarker = "--process-marker";
        var processArguments = new[] { processMarker };
        War3LaunchRequest.Clear();
        var untouched = War3LaunchRequest.ConsumeArguments(processArguments);
        War3LaunchRequest.RequestInteractiveStress();
        var launched = War3LaunchRequest.ConsumeArguments(processArguments);
        var consumed = War3LaunchRequest.ConsumeArguments(processArguments);

        var required = new[]
        {
            War3LaunchRequest.InteractiveStressArgument,
            War3AutomatedSkirmishStressMap.EnableArgument,
            "--war3-auto-skirmish-map-columns=320",
            "--war3-auto-skirmish-map-rows=160",
            "--war3-auto-skirmish-ticks-per-frame=1",
            "--war3-runtime-profile",
            "--war3-profile-variant=interactive-rendered-800",
            "--war3-profile-no-quit",
            "--war3-stress-test",
            "--war3-stress-units-per-team=400",
            "--war3-stress-army-inset=1050",
            "--war3-stress-contact-seconds=5",
            "--war3-stress-quality-report=300",
            "--war3-stress-builders=48",
            "--war3-stress-build-slots=96"
        };
        var complete = required.All(launched.Contains);
        var processPreserved = launched.Contains(processMarker);
        var oneShot = !consumed.Contains(
            War3LaunchRequest.InteractiveStressArgument);
        var profilerEnabled = War3RuntimeProfiler.TryCreate(launched) is not null;
        var externalProfilerDisabled = War3RuntimeProfiler.TryCreate(
            [.. launched, War3RuntimeProfiler.ExternalDotnetArgument]) is null;
        var baselineUntouched = ReferenceEquals(untouched, processArguments);
        War3LaunchRequest.Clear();

        var passed = complete && processPreserved && oneShot &&
                     profilerEnabled && externalProfilerDisabled &&
                     baselineUntouched;
        return new SelfTestResult(
            passed,
            $"complete={complete}, process={processPreserved}, " +
            $"oneShot={oneShot}, profilerEnabled={profilerEnabled}, " +
            $"externalDisabled={externalProfilerDisabled}, " +
            $"baseline={baselineUntouched}, args={launched.Length}");
    }
}
