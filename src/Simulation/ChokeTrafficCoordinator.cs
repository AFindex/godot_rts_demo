using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct ChokeTrafficSnapshot(
    int ChokeId,
    sbyte ActiveDirection,
    bool Draining,
    int PositiveQueue,
    int NegativeQueue,
    int InFlight,
    int PositiveWaitTicks,
    int NegativeWaitTicks,
    int BurstTicks,
    int Capacity,
    int ConflictTicks,
    int MaximumPositiveWaitTicks,
    int MaximumNegativeWaitTicks,
    int HardBlockers,
    int BlockedTicks);

internal readonly record struct ChokeTrafficStateSnapshot(
    sbyte ActiveDirection,
    sbyte LastDirection,
    bool Draining,
    int ClearTicks,
    int BurstTicks,
    int PositiveWaitTicks,
    int NegativeWaitTicks,
    int Capacity,
    int ConflictTicks,
    int MaximumPositiveWaitTicks,
    int MaximumNegativeWaitTicks,
    int BlockedTicks);

internal sealed record ChokeTrafficRuntimeSnapshot(
    ChokeTrafficStateSnapshot[] States,
    ChokeTrafficSnapshot[] Diagnostics);

public sealed class ChokeTrafficCoordinator
{
    private const int MinimumBurstTicks = 90;
    private const int MaximumBurstTicks = 300;
    private const int StarvationTicks = 210;
    private const int ClearTicksBeforeSwitch = 8;

    private readonly TrafficState[] _states;
    private readonly List<int>[] _positiveCandidates;
    private readonly List<int>[] _negativeCandidates;
    private readonly ChokeTrafficSnapshot[] _snapshots;

    public ChokeTrafficCoordinator(int chokeCount)
    {
        _states = new TrafficState[chokeCount];
        _positiveCandidates = new List<int>[chokeCount];
        _negativeCandidates = new List<int>[chokeCount];
        _snapshots = new ChokeTrafficSnapshot[chokeCount];
        for (var choke = 0; choke < chokeCount; choke++)
        {
            _positiveCandidates[choke] = [];
            _negativeCandidates[choke] = [];
        }
    }

    public ReadOnlySpan<ChokeTrafficSnapshot> Snapshots => _snapshots;

    internal ChokeTrafficRuntimeSnapshot CaptureRuntimeState()
    {
        var states = new ChokeTrafficStateSnapshot[_states.Length];
        for (var index = 0; index < states.Length; index++)
        {
            var state = _states[index];
            states[index] = new ChokeTrafficStateSnapshot(
                state.ActiveDirection,
                state.LastDirection,
                state.Draining,
                state.ClearTicks,
                state.BurstTicks,
                state.PositiveWaitTicks,
                state.NegativeWaitTicks,
                state.Capacity,
                state.ConflictTicks,
                state.MaximumPositiveWaitTicks,
                state.MaximumNegativeWaitTicks,
                state.BlockedTicks);
        }
        return new ChokeTrafficRuntimeSnapshot(states, _snapshots.ToArray());
    }

    internal void RestoreRuntimeState(ChokeTrafficRuntimeSnapshot snapshot)
    {
        if (snapshot.States.Length != _states.Length ||
            snapshot.Diagnostics.Length != _snapshots.Length)
        {
            throw new InvalidOperationException("Choke runtime snapshot size mismatch.");
        }
        for (var index = 0; index < _states.Length; index++)
        {
            var source = snapshot.States[index];
            _states[index] = new TrafficState
            {
                ActiveDirection = source.ActiveDirection,
                LastDirection = source.LastDirection,
                Draining = source.Draining,
                ClearTicks = source.ClearTicks,
                BurstTicks = source.BurstTicks,
                PositiveWaitTicks = source.PositiveWaitTicks,
                NegativeWaitTicks = source.NegativeWaitTicks,
                Capacity = source.Capacity,
                ConflictTicks = source.ConflictTicks,
                MaximumPositiveWaitTicks = source.MaximumPositiveWaitTicks,
                MaximumNegativeWaitTicks = source.MaximumNegativeWaitTicks,
                BlockedTicks = source.BlockedTicks
            };
            _snapshots[index] = snapshot.Diagnostics[index];
            _positiveCandidates[index].Clear();
            _negativeCandidates[index].Clear();
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_states.Length);
        for (var index = 0; index < _states.Length; index++)
        {
            var state = _states[index];
            hash.Add((byte)state.ActiveDirection);
            hash.Add((byte)state.LastDirection);
            hash.Add(state.Draining);
            hash.Add(state.ClearTicks);
            hash.Add(state.BurstTicks);
            hash.Add(state.PositiveWaitTicks);
            hash.Add(state.NegativeWaitTicks);
            hash.Add(state.Capacity);
            hash.Add(state.ConflictTicks);
            hash.Add(state.MaximumPositiveWaitTicks);
            hash.Add(state.MaximumNegativeWaitTicks);
            hash.Add(state.BlockedTicks);
        }
    }

    public void Update(UnitStore units, ReadOnlySpan<ChokeDefinition> definitions)
    {
        var positiveInside = new int[_states.Length];
        var negativeInside = new int[_states.Length];
        var largestRadii = new float[_states.Length];
        var hardBlockers = new int[_states.Length];
        for (var choke = 0; choke < _states.Length; choke++)
        {
            _positiveCandidates[choke].Clear();
            _negativeCandidates[choke].Clear();
        }

        for (var unit = 0; unit < units.Count; unit++)
        {
            var chokeId = units.ActiveChokeIds[unit];
            var direction = units.ChokeDirections[unit];
            var phase = units.ChokePhases[unit];
            if ((uint)chokeId >= (uint)_states.Length || direction == 0 ||
                units.Modes[unit] != UnitMoveMode.Moving || phase == ChokePhase.None)
            {
                units.ChokeAdmitted[unit] = false;
                units.ChokeQueueRanks[unit] = -1;
                units.ChokeWaitTicks[unit] = 0;
                continue;
            }

            largestRadii[chokeId] = MathF.Max(largestRadii[chokeId], units.Radii[unit]);
            if (phase is ChokePhase.Traversing or ChokePhase.Exiting)
            {
                units.ChokeAdmitted[unit] = true;
                if (direction > 0)
                {
                    positiveInside[chokeId]++;
                }
                else
                {
                    negativeInside[chokeId]++;
                }
            }
            else if (direction > 0)
            {
                _positiveCandidates[chokeId].Add(unit);
            }
            else
            {
                _negativeCandidates[chokeId].Add(unit);
            }
        }

        for (var unit = 0; unit < units.Count; unit++)
        {
            if (units.Modes[unit] != UnitMoveMode.Hold)
            {
                continue;
            }

            for (var chokeId = 0; chokeId < definitions.Length; chokeId++)
            {
                var definition = definitions[chokeId];
                var relative = units.Positions[unit] - definition.A;
                var along = Vector2.Dot(relative, definition.Axis);
                var lateral = MathF.Abs(Vector2.Dot(relative, definition.Normal));
                if (along >= -units.Radii[unit] &&
                    along <= definition.Length + units.Radii[unit] &&
                    lateral <= definition.Width * 0.5f + units.Radii[unit])
                {
                    hardBlockers[chokeId]++;
                }
            }
        }

        for (var chokeId = 0; chokeId < _states.Length; chokeId++)
        {
            UpdateChoke(
                chokeId,
                definitions[chokeId],
                units,
                positiveInside[chokeId],
                negativeInside[chokeId],
                largestRadii[chokeId],
                hardBlockers[chokeId]);
        }
    }

    private void UpdateChoke(
        int chokeId,
        ChokeDefinition definition,
        UnitStore units,
        int positiveInside,
        int negativeInside,
        float largestRadius,
        int hardBlockers)
    {
        var state = _states[chokeId];
        var positive = _positiveCandidates[chokeId];
        var negative = _negativeCandidates[chokeId];
        SortByEntryDistance(positive, units, definition, 1);
        SortByEntryDistance(negative, units, definition, -1);

        if (state.ActiveDirection == 0)
        {
            state.ActiveDirection = ChooseDirection(
                state,
                positive.Count,
                negative.Count,
                positiveInside,
                negativeInside);
            state.BurstTicks = 0;
            state.Draining = false;
        }

        state.PositiveWaitTicks = positive.Count > 0 && state.ActiveDirection < 0
            ? state.PositiveWaitTicks + 1
            : 0;
        state.NegativeWaitTicks = negative.Count > 0 && state.ActiveDirection > 0
            ? state.NegativeWaitTicks + 1
            : 0;
        state.MaximumPositiveWaitTicks = Math.Max(
            state.MaximumPositiveWaitTicks, state.PositiveWaitTicks);
        state.MaximumNegativeWaitTicks = Math.Max(
            state.MaximumNegativeWaitTicks, state.NegativeWaitTicks);
        if (positiveInside > 0 && negativeInside > 0)
        {
            state.ConflictTicks++;
        }
        if (state.ActiveDirection != 0)
        {
            state.BurstTicks++;
        }

        if (hardBlockers > 0)
        {
            state.Draining = true;
            state.BlockedTicks++;
        }

        var activeInside = state.ActiveDirection > 0 ? positiveInside : negativeInside;
        var oppositeInside = state.ActiveDirection > 0 ? negativeInside : positiveInside;
        var activeCandidates = state.ActiveDirection > 0 ? positive : negative;
        var oppositeCandidates = state.ActiveDirection > 0 ? negative : positive;
        var oppositeWait = state.ActiveDirection > 0
            ? state.NegativeWaitTicks
            : state.PositiveWaitTicks;

        if (!state.Draining && state.ActiveDirection != 0 &&
            state.BurstTicks >= MinimumBurstTicks && oppositeCandidates.Count > 0 &&
            (state.BurstTicks >= MaximumBurstTicks ||
             oppositeWait >= StarvationTicks ||
             activeCandidates.Count == 0))
        {
            state.Draining = true;
        }

        if (state.Draining)
        {
            SetQueueAdmissions(activeCandidates, units, admittedCount: 0);
            SetQueueAdmissions(oppositeCandidates, units, admittedCount: 0);
            if (hardBlockers == 0 && activeInside == 0 && oppositeInside == 0)
            {
                state.ClearTicks++;
                if (state.ClearTicks >= ClearTicksBeforeSwitch)
                {
                    state.LastDirection = state.ActiveDirection;
                    state.ActiveDirection = oppositeCandidates.Count > 0
                        ? (sbyte)-state.ActiveDirection
                        : activeCandidates.Count > 0
                            ? state.ActiveDirection
                            : (sbyte)0;
                    state.Draining = false;
                    state.ClearTicks = 0;
                    state.BurstTicks = 0;
                    state.PositiveWaitTicks = 0;
                    state.NegativeWaitTicks = 0;
                }
            }
            else
            {
                state.ClearTicks = 0;
            }
        }
        else
        {
            state.ClearTicks = 0;
            var radius = largestRadius > 0f ? largestRadius : 7.5f;
            var laneSpacing = radius * 2f + 3f;
            var laneCount = Math.Max(1, (int)MathF.Floor(
                (definition.Width - radius * 2f) / laneSpacing) + 1);
            var capacity = Math.Max(laneCount, laneCount * 4);
            var admittedCount = Math.Max(0, capacity - activeInside - oppositeInside);
            SetQueueAdmissions(activeCandidates, units, admittedCount);
            SetQueueAdmissions(oppositeCandidates, units, admittedCount: 0);
            state.Capacity = capacity;
        }

        var positiveQueued = CountQueued(positive, units);
        var negativeQueued = CountQueued(negative, units);
        var admittedApproaching = positive.Count + negative.Count -
                                  positiveQueued - negativeQueued;
        _snapshots[chokeId] = new ChokeTrafficSnapshot(
            chokeId,
            state.ActiveDirection,
            state.Draining,
            positiveQueued,
            negativeQueued,
            positiveInside + negativeInside + admittedApproaching,
            state.PositiveWaitTicks,
            state.NegativeWaitTicks,
            state.BurstTicks,
            state.Capacity,
            state.ConflictTicks,
            state.MaximumPositiveWaitTicks,
            state.MaximumNegativeWaitTicks,
            hardBlockers,
            state.BlockedTicks);
        _states[chokeId] = state;
    }

    private static void SetQueueAdmissions(
        List<int> candidates,
        UnitStore units,
        int admittedCount)
    {
        var queueRank = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var unit = candidates[index];
            var admitted = index < admittedCount;
            units.ChokeAdmitted[unit] = admitted;
            units.ChokeQueueRanks[unit] = admitted ? -1 : queueRank++;
            units.ChokeWaitTicks[unit] = admitted
                ? 0
                : units.ChokeWaitTicks[unit] + 1;
        }
    }

    private static int CountQueued(List<int> candidates, UnitStore units)
    {
        var count = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!units.ChokeAdmitted[candidates[index]])
            {
                count++;
            }
        }

        return count;
    }

    private static void SortByEntryDistance(
        List<int> candidates,
        UnitStore units,
        ChokeDefinition definition,
        sbyte direction)
    {
        var travelAxis = definition.Axis * direction;
        var entry = direction > 0 ? definition.A : definition.B;
        candidates.Sort((left, right) =>
        {
            var leftAlong = Vector2.Dot(units.Positions[left] - entry, travelAxis);
            var rightAlong = Vector2.Dot(units.Positions[right] - entry, travelAxis);
            var compare = rightAlong.CompareTo(leftAlong);
            return compare != 0 ? compare : left.CompareTo(right);
        });
    }

    private static sbyte ChooseDirection(
        TrafficState state,
        int positiveQueue,
        int negativeQueue,
        int positiveInside,
        int negativeInside)
    {
        if (positiveInside > 0 && negativeInside == 0)
        {
            return 1;
        }

        if (negativeInside > 0 && positiveInside == 0)
        {
            return -1;
        }

        if (positiveQueue == 0 && negativeQueue == 0)
        {
            return 0;
        }

        if (positiveQueue == 0)
        {
            return -1;
        }

        if (negativeQueue == 0)
        {
            return 1;
        }

        if (state.PositiveWaitTicks != state.NegativeWaitTicks)
        {
            return state.PositiveWaitTicks > state.NegativeWaitTicks ? (sbyte)1 : (sbyte)-1;
        }

        if (positiveQueue != negativeQueue)
        {
            return positiveQueue > negativeQueue ? (sbyte)1 : (sbyte)-1;
        }

        return state.LastDirection >= 0 ? (sbyte)-1 : (sbyte)1;
    }

    private struct TrafficState
    {
        public sbyte ActiveDirection;
        public sbyte LastDirection;
        public bool Draining;
        public int ClearTicks;
        public int BurstTicks;
        public int PositiveWaitTicks;
        public int NegativeWaitTicks;
        public int Capacity;
        public int ConflictTicks;
        public int MaximumPositiveWaitTicks;
        public int MaximumNegativeWaitTicks;
        public int BlockedTicks;
    }
}
