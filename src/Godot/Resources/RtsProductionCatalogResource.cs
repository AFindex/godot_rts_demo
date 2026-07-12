using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsProductionCatalogResource : Resource
{
    [Export] public int FormatVersion { get; set; } = 5;
    [Export] public Array<UnitTypeProfileResource> UnitTypes { get; set; } = new();
    [Export] public Array<ProductionRecipeProfileResource> Recipes { get; set; } = new();
}
