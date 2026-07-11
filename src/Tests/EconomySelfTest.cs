using System.Numerics;

namespace RtsDemo.Tests;

public readonly record struct EconomyScenarioFixture(
    MovementTestRig Rig,
    TestUnitId[] Workers,
    TestResourceNodeId FirstMineral,
    TestResourceNodeId ReserveMineral,
    TestResourceNodeId SecondMineral,
    TestResourceNodeId Gas,
    TestPlayerEconomySnapshot StartingEconomy,
    bool TransactionRulesPassed,
    bool CommandRulesPassed);

public static class EconomySelfTest
{
    public static SelfTestResult Run()
    {
        var fixture = CreateScenario();
        while (fixture.Rig.Tick < 1200)
        {
            fixture.Rig.Step();
        }
        return Evaluate(fixture);
    }

    public static EconomyScenarioFixture CreateScenario()
    {
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1200f, 700f), 32);
        rig.RegisterPlayer(1, 500, 25, 15, 12);
        rig.RegisterPlayer(2, 50, 0, 10);

        var hashBeforeSpend = rig.StateHash;
        var spend = rig.Spend(1, 100, 10, 2);
        var hashAfterSpend = rig.StateHash;
        var afterSpend = rig.ObservePlayerEconomy(1);
        var gasRejected = rig.Spend(1, 25, 50, 0);
        var supplyRejected = rig.Spend(1, 10, 0, 2);
        var afterRejects = rig.ObservePlayerEconomy(1);
        rig.Refund(1, 100, 10, 2, fraction: 0.75f);
        var starting = rig.ObservePlayerEconomy(1);
        var transactions =
            spend == TestEconomyTransactionCode.Success &&
            hashBeforeSpend != hashAfterSpend &&
            afterSpend == new TestPlayerEconomySnapshot(400, 15, 14, 15) &&
            gasRejected == TestEconomyTransactionCode.InsufficientVespeneGas &&
            supplyRejected == TestEconomyTransactionCode.SupplyBlocked &&
            afterRejects == afterSpend &&
            starting == new TestPlayerEconomySnapshot(475, 22, 12, 15);

        rig.AddResourceDropOff(1, new Vector2(170f, 350f));
        var firstMineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(430f, 245f),
            amount: 5,
            harvestBatch: 5,
            harvestSeconds: 0.35f,
            harvesterCapacity: 1);
        var reserveMineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(505f, 245f),
            amount: 180,
            harvestBatch: 5,
            harvestSeconds: 0.35f,
            harvesterCapacity: 1);
        var secondMineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(475f, 355f),
            amount: 180,
            harvestBatch: 5,
            harvestSeconds: 0.35f,
            harvesterCapacity: 1);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(470f, 500f),
            amount: 180,
            harvestBatch: 4,
            harvestSeconds: 0.45f,
            harvesterCapacity: 3,
            requiresRefinery: true,
            operational: false);

        var workers = new TestUnitId[8];
        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = rig.SpawnWorker(
                new Vector2(130f + index * 16f, 330f + index % 2 * 24f), 1);
        }
        var foreignWorker = rig.SpawnWorker(new Vector2(120f, 590f), 2);
        var cancelledWorker = rig.SpawnWorker(new Vector2(150f, 610f), 1);
        var refineryRequired = rig.Gather(1, workers[5], gas);
        var wrongOwner = rig.Gather(1, foreignWorker, firstMineral);
        var gatherBeforeCancel = rig.Gather(1, cancelledWorker, reserveMineral);
        rig.Move([cancelledWorker], new Vector2(300f, 620f));
        var cancelled = rig.ObserveWorkerEconomy(cancelledWorker).State ==
                        TestWorkerEconomyState.Idle;
        rig.SetRefineryOperational(gas, true);

        var commands = new[]
        {
            rig.Gather(1, workers[0], firstMineral),
            rig.Gather(1, workers[1], firstMineral),
            rig.Gather(1, workers[2], reserveMineral),
            rig.Gather(1, workers[3], secondMineral),
            rig.Gather(1, workers[4], secondMineral),
            rig.Gather(1, workers[5], gas),
            rig.Gather(1, workers[6], gas),
            rig.Gather(1, workers[7], gas)
        };
        var commandRules =
            refineryRequired == TestGatherCommandCode.RefineryRequired &&
            wrongOwner == TestGatherCommandCode.WrongOwner &&
            gatherBeforeCancel == TestGatherCommandCode.Success && cancelled &&
            commands.All(value => value == TestGatherCommandCode.Success);

        return new EconomyScenarioFixture(
            rig,
            workers,
            firstMineral,
            reserveMineral,
            secondMineral,
            gas,
            starting,
            transactions,
            commandRules);
    }

    public static SelfTestResult Evaluate(EconomyScenarioFixture fixture)
    {
        var economy = fixture.Rig.ObservePlayerEconomy(1);
        var first = fixture.Rig.ObserveResourceNode(fixture.FirstMineral);
        var reserve = fixture.Rig.ObserveResourceNode(fixture.ReserveMineral);
        var second = fixture.Rig.ObserveResourceNode(fixture.SecondMineral);
        var gas = fixture.Rig.ObserveResourceNode(fixture.Gas);
        var activeWorkers = fixture.Workers.Count(worker =>
            fixture.Rig.ObserveWorkerEconomy(worker).State is not
                (TestWorkerEconomyState.None or TestWorkerEconomyState.Idle));
        var gainedMinerals = economy.Minerals - fixture.StartingEconomy.Minerals;
        var gainedGas = economy.VespeneGas - fixture.StartingEconomy.VespeneGas;
        var passed = fixture.TransactionRulesPassed &&
                     fixture.CommandRulesPassed &&
                     gainedMinerals >= 30 && gainedGas >= 24 &&
                     first.Remaining == 0 && reserve.Remaining < 180 &&
                     second.Remaining < 180 && gas.Remaining < 180 &&
                     gas.Operational && activeWorkers >= 6 &&
                     first.ActiveHarvesters <= first.HarvesterCapacity &&
                     reserve.ActiveHarvesters <= reserve.HarvesterCapacity &&
                     second.ActiveHarvesters <= second.HarvesterCapacity &&
                     gas.ActiveHarvesters <= gas.HarvesterCapacity;
        return new SelfTestResult(
            passed,
            $"minerals=+{gainedMinerals}, gas=+{gainedGas}, " +
            $"depleted={first.Remaining == 0}, reserve={reserve.Remaining}, " +
            $"gasLeft={gas.Remaining}, active={activeWorkers}/8, " +
            $"transactions={fixture.TransactionRulesPassed}, " +
            $"commands={fixture.CommandRulesPassed}");
    }
}
