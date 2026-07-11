using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsAiConfigurationCatalogResource : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public Array<AiDifficultyProfileResource> Profiles { get; set; } = new();
}
