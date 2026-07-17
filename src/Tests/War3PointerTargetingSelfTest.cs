using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using War3Rts;
using GVector2 = Godot.Vector2;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Tests;

public static class War3PointerTargetingSelfTest
{
    public static SelfTestResult Run()
    {
        var bounds = new SimRect(
            new NVector2(100f, 200f),
            new NVector2(212f, 292f));

        var center = War3PointerTargeting.HitsBuilding(
            bounds, new NVector2(156f, 246f));
        var insideEdge = War3PointerTargeting.HitsBuilding(
            bounds, new NVector2(211.99f, 246f));
        var outsideNear = War3PointerTargeting.HitsBuilding(
            bounds, new NVector2(212.01f, 246f));
        var outsideFormerSnapRing = War3PointerTargeting.HitsBuilding(
            bounds, new NVector2(250f, 246f));
        var bottomHudAllowsEdgeScroll = !War3PointerTargeting.BlocksCameraEdgeScroll(
            hudBlocksWorldPointer: true,
            navigationDebuggerBlocksWorldPointer: false);
        var debuggerBlocksEdgeScroll = War3PointerTargeting.BlocksCameraEdgeScroll(
            hudBlocksWorldPointer: false,
            navigationDebuggerBlocksWorldPointer: true);
        var belowZeroTerrain = FlatFineHeightTerrain(-24f);
        var belowZeroRay = War3PointerTargeting.TryIntersectTerrainRay(
            belowZeroTerrain,
            new Vector3(0.8f, 4f, 0.8f),
            Vector3.Down,
            out var belowZeroPoint) &&
            NVector2.DistanceSquared(
                belowZeroPoint, new NVector2(32f, 32f)) < 0.01f;
        var highFineTerrain = FlatFineHeightTerrain(96f);
        var highFineRay = War3PointerTargeting.TryIntersectTerrainRay(
            highFineTerrain,
            new Vector3(0.8f, 5f, 0.8f),
            Vector3.Down,
            out var highFinePoint) &&
            NVector2.DistanceSquared(
                highFinePoint, new NVector2(32f, 32f)) < 0.01f;
        var camera = new Rts3DCameraController();
        var dragPressed = camera.CaptureMiddleMouseDragInput(
                              new InputEventMouseButton
                              {
                                  ButtonIndex = MouseButton.Middle,
                                  Pressed = true
                              }) && camera.MiddleMouseDragActive;
        var dragMoved = camera.CaptureMiddleMouseDragInput(
                            new InputEventMouseMotion
                            {
                                Relative = new GVector2(0f, 18f)
                            }) &&
                        camera.PendingPanPixels.IsEqualApprox(
                            new GVector2(0f, 18f));
        var dragReleased = camera.CaptureMiddleMouseDragInput(
                               new InputEventMouseButton
                               {
                                   ButtonIndex = MouseButton.Middle,
                                   Pressed = false
                               }) && !camera.MiddleMouseDragActive;
        camera.Free();

        var passed = center && insideEdge &&
                     !outsideNear && !outsideFormerSnapRing &&
                     bottomHudAllowsEdgeScroll && debuggerBlocksEdgeScroll &&
                     belowZeroRay && highFineRay &&
                     dragPressed && dragMoved && dragReleased;
        return new SelfTestResult(
            passed,
            $"center={center}, edge={insideEdge}, " +
            $"outside={outsideNear}, formerSnap={outsideFormerSnapRing}, " +
            $"bottomEdge={bottomHudAllowsEdgeScroll}, " +
            $"debugBlock={debuggerBlocksEdgeScroll}, " +
            $"terrainRay={belowZeroRay}/{highFineRay}, " +
            $"middleDrag={dragPressed}/{dragMoved}/{dragReleased}");
    }

    private static TerrainMapSnapshot FlatFineHeightTerrain(float height)
    {
        var bounds = new SimRect(NVector2.Zero, new NVector2(64f, 64f));
        TerrainSurfaceDefinition[] surfaces = [new(0, "ground", "Ground")];
        var cell = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(cell, 4).ToArray();
        var fineHeights = Enumerable.Repeat(height, 9).ToArray();
        if (!TerrainMapSnapshot.TryCreate(
                bounds, 32f, 48f, surfaces, cells, fineHeights,
                out var terrain, out var validation) || terrain is null)
        {
            throw new InvalidOperationException(
                $"Pointer terrain fixture invalid: {validation.FirstError}");
        }
        return terrain;
    }
}
