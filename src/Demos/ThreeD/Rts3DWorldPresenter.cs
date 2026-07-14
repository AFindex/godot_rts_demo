using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Read-only 3D projection of an <see cref="RtsSimulation"/>. The presenter
/// owns only Godot visual nodes and never issues commands or mutates simulation
/// state, so it can be replaced without coupling gameplay to the 3D scene.
/// </summary>
public partial class Rts3DWorldPresenter : Node3D
{
    private const float MinimumUnitRadius = 0.16f;
    private const float ProjectileHeight = 0.62f;
    private const float SelectionHeight = 0.035f;

    private readonly Dictionary<int, UnitVisual> _units = [];
    private readonly Dictionary<int, BuildingVisual> _buildings = [];
    private readonly Dictionary<int, ResourceVisual> _resources = [];
    private readonly Dictionary<int, ProjectileVisual> _projectiles = [];
    private readonly List<MeshInstance3D> _obstacles = [];
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private readonly HashSet<int> _seenBuildings = [];
    private readonly HashSet<int> _seenResources = [];
    private readonly HashSet<int> _seenProjectiles = [];

    private readonly Dictionary<UnitVisualKind, Mesh> _unitMeshes = [];
    private readonly Dictionary<BuildingFunctionKind, Mesh> _buildingMeshes = [];
    private readonly Dictionary<EconomyResourceKind, Mesh> _resourceMeshes = [];
    private readonly Dictionary<(UnitVisualKind Kind, int Team), StandardMaterial3D>
        _unitMaterials = [];
    private readonly Dictionary<(BuildingFunctionKind Function, int Team),
        StandardMaterial3D> _buildingMaterials = [];
    private readonly Dictionary<int, StandardMaterial3D> _projectileMaterials = [];

    private RtsSimulation? _simulation;
    private Mesh? _obstacleMesh;
    private Mesh? _selectionMesh;
    private Mesh? _projectileMesh;
    private StandardMaterial3D? _obstacleMaterial;
    private StandardMaterial3D? _selectionMaterial;
    private StandardMaterial3D? _mineralMaterial;
    private StandardMaterial3D? _vespeneMaterial;

    public int PresentedEntityCount =>
        _units.Count + _buildings.Count + _resources.Count + _projectiles.Count;

    public void Initialize(RtsSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ClearVisuals();
        _simulation = simulation;
        CreateStaticObstacles(simulation.World);
        Sync();
    }

    public void SetSelection(
        IEnumerable<int> unitIds,
        IEnumerable<int> buildingIds)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        ArgumentNullException.ThrowIfNull(buildingIds);

        _selectedUnits.Clear();
        _selectedUnits.UnionWith(unitIds);
        _selectedBuildings.Clear();
        _selectedBuildings.UnionWith(buildingIds);
        SyncSelectionVisibility();
    }

    public void Sync(float interpolation = 1f)
    {
        if (_simulation is null)
        {
            return;
        }

        interpolation = Math.Clamp(interpolation, 0f, 1f);
        SyncUnits(_simulation, interpolation);
        SyncBuildings(_simulation);
        SyncResources(_simulation);
        SyncProjectiles(_simulation);
    }

    private void SyncUnits(RtsSimulation simulation, float interpolation)
    {
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (!simulation.Units.Alive[unit])
            {
                RemoveUnit(unit);
                continue;
            }

            var kind = UnitKind(simulation, unit);
            var team = simulation.Combat.Teams[unit];
            if (!_units.TryGetValue(unit, out var visual))
            {
                visual = CreateUnitVisual(unit, kind, team);
                _units.Add(unit, visual);
            }
            else if (visual.Kind != kind || visual.Team != team)
            {
                visual.Kind = kind;
                visual.Team = team;
                visual.Body.Mesh = UnitMesh(kind);
                visual.Body.MaterialOverride = UnitMaterial(kind, team);
            }

            var simulationPosition = NVector2.Lerp(
                simulation.Units.PreviousPositions[unit],
                simulation.Units.Positions[unit],
                interpolation);
            var radius = MathF.Max(
                MinimumUnitRadius,
                SimPlane3DTransform.ToWorldLength(
                    simulation.Units.Radii[unit]));
            var height = UnitHeight(kind, radius);
            var worldPosition = SimPlane3DTransform.ToWorld(
                simulationPosition, height * 0.5f);
            visual.Body.Position = worldPosition;
            visual.Body.Scale = new Vector3(radius * 2f, height, radius * 2f);

            var velocity = simulation.Units.Velocities[unit];
            if (velocity.LengthSquared() > 1f)
            {
                visual.Body.Rotation = new Vector3(
                    0f, MathF.Atan2(velocity.X, velocity.Y), 0f);
            }

            visual.Selection.Position = new Vector3(
                worldPosition.X, SelectionHeight, worldPosition.Z);
            visual.Selection.Scale = Vector3.One * (radius * 2.65f);
            visual.Selection.Visible = _selectedUnits.Contains(unit);
        }
    }

    private void SyncBuildings(RtsSimulation simulation)
    {
        _seenBuildings.Clear();
        foreach (var building in simulation.CreateGameplayBuildingOverview())
        {
            if (building.IsTerminal)
            {
                continue;
            }

            var id = building.Id.Value;
            _seenBuildings.Add(id);
            if (!_buildings.TryGetValue(id, out var visual))
            {
                visual = CreateBuildingVisual(building);
                _buildings.Add(id, visual);
            }
            else if (visual.Function != building.Type.Function ||
                     visual.Team != building.PlayerId)
            {
                visual.Function = building.Type.Function;
                visual.Team = building.PlayerId;
                visual.Body.Mesh = BuildingMesh(building.Type.Function);
                visual.Body.MaterialOverride = BuildingMaterial(
                    building.Type.Function, building.PlayerId);
            }

            var size = SimPlane3DTransform.ToWorldSize(
                building.Bounds.Max - building.Bounds.Min);
            var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
            var fullHeight = BuildingHeight(building.Type.Function, size);
            var constructionScale = building.State ==
                                    BuildingLifecycleState.Completed
                ? 1f
                : MathF.Max(0.18f, building.Progress);
            var height = fullHeight * constructionScale;
            var position = SimPlane3DTransform.ToWorld(center, height * 0.5f);
            visual.Body.Position = position;
            visual.Body.Scale = new Vector3(size.X, height, size.Y);

            var markerDiameter = MathF.Max(size.X, size.Y) * 1.18f;
            visual.Selection.Position = new Vector3(
                position.X, SelectionHeight, position.Z);
            visual.Selection.Scale = Vector3.One * markerDiameter;
            visual.Selection.Visible = _selectedBuildings.Contains(id);
        }

        RemoveUnseen(_buildings, _seenBuildings, static visual =>
        {
            visual.Body.QueueFree();
            visual.Selection.QueueFree();
        });
    }

    private void SyncResources(RtsSimulation simulation)
    {
        _seenResources.Clear();
        for (var index = 0; index < simulation.Economy.ResourceNodeCount; index++)
        {
            var node = simulation.Economy.ObserveResourceNode(
                new EconomyResourceNodeId(index));
            if (node.Remaining <= 0)
            {
                continue;
            }

            _seenResources.Add(index);
            if (!_resources.TryGetValue(index, out var visual))
            {
                visual = CreateResourceVisual(index, node.Kind);
                _resources.Add(index, visual);
            }
            else if (visual.Kind != node.Kind)
            {
                visual.Kind = node.Kind;
                visual.Body.Mesh = ResourceMesh(node.Kind);
                visual.Body.MaterialOverride = ResourceMaterial(node.Kind);
            }

            var size = node.Kind == EconomyResourceKind.Minerals
                ? new Vector3(0.48f, 0.72f, 0.34f)
                : new Vector3(0.72f, 0.62f, 0.72f);
            visual.Body.Position = SimPlane3DTransform.ToWorld(
                node.Position, size.Y * 0.5f);
            visual.Body.Scale = size;
            visual.Body.Visible = true;
        }

        RemoveUnseen(_resources, _seenResources,
            static visual => visual.Body.QueueFree());
    }

    private void SyncProjectiles(RtsSimulation simulation)
    {
        _seenProjectiles.Clear();
        foreach (var projectile in simulation.CombatProjectiles.ObserveActive())
        {
            _seenProjectiles.Add(projectile.Id);
            var team = (uint)projectile.AttackerUnit <
                       (uint)simulation.Units.Count
                ? simulation.Combat.Teams[projectile.AttackerUnit]
                : 0;
            if (!_projectiles.TryGetValue(projectile.Id, out var visual))
            {
                visual = CreateProjectileVisual(projectile.Id, team);
                _projectiles.Add(projectile.Id, visual);
            }
            else if (visual.Team != team)
            {
                visual.Team = team;
                visual.Body.MaterialOverride = ProjectileMaterial(team);
            }

            visual.Body.Position = SimPlane3DTransform.ToWorld(
                projectile.Position, ProjectileHeight);
        }

        RemoveUnseen(_projectiles, _seenProjectiles,
            static visual => visual.Body.QueueFree());
    }

    private UnitVisual CreateUnitVisual(
        int unit,
        UnitVisualKind kind,
        int team)
    {
        var body = new MeshInstance3D
        {
            Name = $"Unit{unit}",
            Mesh = UnitMesh(kind),
            MaterialOverride = UnitMaterial(kind, team)
        };
        var selection = CreateSelectionMarker($"UnitSelection{unit}");
        AddChild(body);
        AddChild(selection);
        return new UnitVisual(body, selection, kind, team);
    }

    private BuildingVisual CreateBuildingVisual(
        GameplayBuildingSnapshot building)
    {
        var body = new MeshInstance3D
        {
            Name = $"Building{building.Id.Value}",
            Mesh = BuildingMesh(building.Type.Function),
            MaterialOverride = BuildingMaterial(
                building.Type.Function, building.PlayerId)
        };
        var selection = CreateSelectionMarker(
            $"BuildingSelection{building.Id.Value}");
        AddChild(body);
        AddChild(selection);
        return new BuildingVisual(
            body, selection, building.Type.Function, building.PlayerId);
    }

    private ResourceVisual CreateResourceVisual(
        int id,
        EconomyResourceKind kind)
    {
        var body = new MeshInstance3D
        {
            Name = $"Resource{id}",
            Mesh = ResourceMesh(kind),
            MaterialOverride = ResourceMaterial(kind)
        };
        AddChild(body);
        return new ResourceVisual(body, kind);
    }

    private ProjectileVisual CreateProjectileVisual(int id, int team)
    {
        var body = new MeshInstance3D
        {
            Name = $"Projectile{id}",
            Mesh = ProjectileMesh(),
            MaterialOverride = ProjectileMaterial(team),
            Scale = Vector3.One * 0.14f
        };
        AddChild(body);
        return new ProjectileVisual(body, team);
    }

    private MeshInstance3D CreateSelectionMarker(string name)
    {
        var marker = new MeshInstance3D
        {
            Name = name,
            Mesh = SelectionMesh(),
            MaterialOverride = SelectionMaterial(),
            Visible = false
        };
        return marker;
    }

    private void CreateStaticObstacles(StaticWorld world)
    {
        foreach (var obstacle in world.Obstacles)
        {
            var size = SimPlane3DTransform.ToWorldSize(
                obstacle.Max - obstacle.Min);
            const float height = 1.55f;
            var center = (obstacle.Min + obstacle.Max) * 0.5f;
            var visual = new MeshInstance3D
            {
                Name = $"Obstacle{_obstacles.Count}",
                Mesh = ObstacleMesh(),
                MaterialOverride = ObstacleMaterial(),
                Position = SimPlane3DTransform.ToWorld(center, height * 0.5f),
                Scale = new Vector3(size.X, height, size.Y)
            };
            AddChild(visual);
            _obstacles.Add(visual);
        }
    }

    private void SyncSelectionVisibility()
    {
        foreach (var (id, visual) in _units)
        {
            visual.Selection.Visible = _selectedUnits.Contains(id);
        }
        foreach (var (id, visual) in _buildings)
        {
            visual.Selection.Visible = _selectedBuildings.Contains(id);
        }
    }

    private void RemoveUnit(int unit)
    {
        if (!_units.Remove(unit, out var visual))
        {
            return;
        }
        visual.Body.QueueFree();
        visual.Selection.QueueFree();
    }

    private void ClearVisuals()
    {
        foreach (var visual in _units.Values)
        {
            visual.Body.QueueFree();
            visual.Selection.QueueFree();
        }
        foreach (var visual in _buildings.Values)
        {
            visual.Body.QueueFree();
            visual.Selection.QueueFree();
        }
        foreach (var visual in _resources.Values)
        {
            visual.Body.QueueFree();
        }
        foreach (var visual in _projectiles.Values)
        {
            visual.Body.QueueFree();
        }
        foreach (var obstacle in _obstacles)
        {
            obstacle.QueueFree();
        }

        _units.Clear();
        _buildings.Clear();
        _resources.Clear();
        _projectiles.Clear();
        _obstacles.Clear();
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
    }

    private Mesh UnitMesh(UnitVisualKind kind)
    {
        if (_unitMeshes.TryGetValue(kind, out var mesh))
        {
            return mesh;
        }

        mesh = kind switch
        {
            UnitVisualKind.Worker => new BoxMesh { Size = Vector3.One },
            UnitVisualKind.Armored => new CylinderMesh
            {
                TopRadius = 0.5f,
                BottomRadius = 0.5f,
                Height = 1f,
                RadialSegments = 6
            },
            _ => new SphereMesh
            {
                Radius = 0.5f,
                Height = 1f,
                RadialSegments = 12,
                Rings = 6
            }
        };
        _unitMeshes.Add(kind, mesh);
        return mesh;
    }

    private Mesh BuildingMesh(BuildingFunctionKind function)
    {
        if (_buildingMeshes.TryGetValue(function, out var mesh))
        {
            return mesh;
        }

        mesh = function switch
        {
            BuildingFunctionKind.Supply => new BoxMesh { Size = Vector3.One },
            BuildingFunctionKind.Production => new PrismMesh
            {
                Size = Vector3.One
            },
            BuildingFunctionKind.TownHall => new CylinderMesh
            {
                TopRadius = 0.5f,
                BottomRadius = 0.5f,
                Height = 1f,
                RadialSegments = 8
            },
            BuildingFunctionKind.Refinery => new CylinderMesh
            {
                TopRadius = 0.36f,
                BottomRadius = 0.5f,
                Height = 1f,
                RadialSegments = 12
            },
            _ => new SphereMesh
            {
                Radius = 0.5f,
                Height = 1f,
                RadialSegments = 10,
                Rings = 5
            }
        };
        _buildingMeshes.Add(function, mesh);
        return mesh;
    }

    private Mesh ResourceMesh(EconomyResourceKind kind)
    {
        if (_resourceMeshes.TryGetValue(kind, out var mesh))
        {
            return mesh;
        }

        mesh = kind == EconomyResourceKind.Minerals
            ? new PrismMesh { Size = Vector3.One }
            : new SphereMesh
            {
                Radius = 0.5f,
                Height = 1f,
                RadialSegments = 10,
                Rings = 5
            };
        _resourceMeshes.Add(kind, mesh);
        return mesh;
    }

    private Mesh ObstacleMesh() =>
        _obstacleMesh ??= new BoxMesh { Size = Vector3.One };

    private Mesh SelectionMesh() =>
        _selectionMesh ??= new TorusMesh
        {
            InnerRadius = 0.40f,
            OuterRadius = 0.50f,
            Rings = 20,
            RingSegments = 6
        };

    private Mesh ProjectileMesh() =>
        _projectileMesh ??= new SphereMesh
        {
            Radius = 0.5f,
            Height = 1f,
            RadialSegments = 8,
            Rings = 4
        };

    private StandardMaterial3D UnitMaterial(UnitVisualKind kind, int team)
    {
        var key = (kind, team);
        if (_unitMaterials.TryGetValue(key, out var material))
        {
            return material;
        }

        var baseColor = TeamColor(team);
        var color = kind switch
        {
            UnitVisualKind.Worker => baseColor.Lightened(0.20f),
            UnitVisualKind.Armored => baseColor.Darkened(0.13f),
            _ => baseColor
        };
        material = OpaqueMaterial(
            color,
            kind == UnitVisualKind.Armored ? 0.32f : 0.08f,
            kind == UnitVisualKind.Armored ? 0.48f : 0.68f);
        _unitMaterials.Add(key, material);
        return material;
    }

    private StandardMaterial3D BuildingMaterial(
        BuildingFunctionKind function,
        int team)
    {
        var key = (function, team);
        if (_buildingMaterials.TryGetValue(key, out var material))
        {
            return material;
        }

        var baseColor = TeamColor(team);
        var color = function switch
        {
            BuildingFunctionKind.TownHall => baseColor.Lightened(0.18f),
            BuildingFunctionKind.Supply => baseColor.Lightened(0.32f),
            BuildingFunctionKind.Production => baseColor.Darkened(0.08f),
            BuildingFunctionKind.Refinery => baseColor.Lerp(
                new Color("59d17d"), 0.28f),
            _ => baseColor.Lerp(new Color("b783ff"), 0.25f)
        };
        material = OpaqueMaterial(color, 0.30f, 0.56f);
        _buildingMaterials.Add(key, material);
        return material;
    }

    private StandardMaterial3D ResourceMaterial(EconomyResourceKind kind)
    {
        if (kind == EconomyResourceKind.Minerals)
        {
            return _mineralMaterial ??= EmissiveMaterial(
                new Color("39bdf2"), 0.24f);
        }
        return _vespeneMaterial ??= EmissiveMaterial(
            new Color("44d879"), 0.20f);
    }

    private StandardMaterial3D ProjectileMaterial(int team)
    {
        if (_projectileMaterials.TryGetValue(team, out var material))
        {
            return material;
        }
        material = EmissiveMaterial(TeamColor(team).Lightened(0.38f), 1.15f);
        _projectileMaterials.Add(team, material);
        return material;
    }

    private StandardMaterial3D ObstacleMaterial() =>
        _obstacleMaterial ??= OpaqueMaterial(
            new Color("344454"), 0.12f, 0.92f);

    private StandardMaterial3D SelectionMaterial() =>
        _selectionMaterial ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.98f, 1f, 0.92f),
            EmissionEnabled = true,
            Emission = new Color("50f5ff"),
            EmissionEnergyMultiplier = 1.3f,
            Roughness = 0.35f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

    private static StandardMaterial3D OpaqueMaterial(
        Color color,
        float metallic,
        float roughness) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = roughness
    };

    private static StandardMaterial3D EmissiveMaterial(
        Color color,
        float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        Metallic = 0.14f,
        Roughness = 0.42f
    };

    private static UnitVisualKind UnitKind(RtsSimulation simulation, int unit)
    {
        if (simulation.Economy.IsWorker(unit))
        {
            return UnitVisualKind.Worker;
        }
        return (simulation.Combat.Attributes[unit] & CombatAttribute.Armored) != 0
            ? UnitVisualKind.Armored
            : UnitVisualKind.Standard;
    }

    private static float UnitHeight(UnitVisualKind kind, float radius) =>
        kind switch
        {
            UnitVisualKind.Worker => radius * 1.35f,
            UnitVisualKind.Armored => radius * 2.10f,
            _ => radius * 1.70f
        };

    private static float BuildingHeight(
        BuildingFunctionKind function,
        Vector2 size)
    {
        var footprint = MathF.Min(size.X, size.Y);
        return function switch
        {
            BuildingFunctionKind.Supply => MathF.Max(0.50f, footprint * 0.42f),
            BuildingFunctionKind.Production => MathF.Max(0.88f, footprint * 0.62f),
            BuildingFunctionKind.TownHall => MathF.Max(1.30f, footprint * 0.72f),
            BuildingFunctionKind.Refinery => MathF.Max(0.90f, footprint * 0.82f),
            _ => MathF.Max(1.02f, footprint * 0.76f)
        };
    }

    private static Color TeamColor(int team) => team switch
    {
        1 => new Color("3d8ef7"),
        2 => new Color("ef4d5b"),
        3 => new Color("f2bb3c"),
        4 => new Color("9c63ef"),
        _ => new Color("a9b4c1")
    };

    private static void RemoveUnseen<T>(
        Dictionary<int, T> values,
        HashSet<int> seen,
        Action<T> release)
    {
        foreach (var id in values.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            var value = values[id];
            values.Remove(id);
            release(value);
        }
    }

    private enum UnitVisualKind : byte
    {
        Worker,
        Standard,
        Armored
    }

    private sealed class UnitVisual(
        MeshInstance3D body,
        MeshInstance3D selection,
        UnitVisualKind kind,
        int team)
    {
        public MeshInstance3D Body { get; } = body;
        public MeshInstance3D Selection { get; } = selection;
        public UnitVisualKind Kind { get; set; } = kind;
        public int Team { get; set; } = team;
    }

    private sealed class BuildingVisual(
        MeshInstance3D body,
        MeshInstance3D selection,
        BuildingFunctionKind function,
        int team)
    {
        public MeshInstance3D Body { get; } = body;
        public MeshInstance3D Selection { get; } = selection;
        public BuildingFunctionKind Function { get; set; } = function;
        public int Team { get; set; } = team;
    }

    private sealed class ResourceVisual(
        MeshInstance3D body,
        EconomyResourceKind kind)
    {
        public MeshInstance3D Body { get; } = body;
        public EconomyResourceKind Kind { get; set; } = kind;
    }

    private sealed class ProjectileVisual(MeshInstance3D body, int team)
    {
        public MeshInstance3D Body { get; } = body;
        public int Team { get; set; } = team;
    }
}
