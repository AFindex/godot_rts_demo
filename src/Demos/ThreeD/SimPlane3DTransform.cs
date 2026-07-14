using Godot;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Converts the simulation's XY ground plane to Godot's XZ ground plane.
/// Keeping this mapping in one place prevents presentation code from leaking
/// Godot coordinates into the deterministic simulation.
/// </summary>
public static class SimPlane3DTransform
{
    public const float WorldScale = 0.025f;

    public static Vector3 ToWorld(NVector2 simulationPosition, float height = 0f) =>
        new(
            simulationPosition.X * WorldScale,
            height,
            simulationPosition.Y * WorldScale);

    public static NVector2 ToSimulation(Vector3 worldPosition) =>
        new(worldPosition.X / WorldScale, worldPosition.Z / WorldScale);

    public static float ToWorldLength(float simulationLength) =>
        simulationLength * WorldScale;

    public static float ToSimulationLength(float worldLength) =>
        worldLength / WorldScale;

    public static Vector2 ToWorldSize(NVector2 simulationSize) =>
        new(simulationSize.X * WorldScale, simulationSize.Y * WorldScale);
}
