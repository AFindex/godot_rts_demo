using War3Rts.Maps;

namespace War3Rts;

/// <summary>
/// Generated only for the opt-in automated stress command line. It is not in
/// the normal map catalog and cannot alter a regular skirmish save or cache.
/// </summary>
internal static class War3AutomatedSkirmishStressMap
{
    public const string EnableArgument = "--war3-auto-skirmish-large-map";
    private const float CellSize = 32f;

    public static bool IsRequested(string[] arguments) =>
        arguments.Contains(EnableArgument);

    public static War3MapRuntime Create(string[] arguments)
    {
        var columns = IntegerArgument(
            arguments, "--war3-auto-skirmish-map-columns=", 256, 128, 512);
        var rows = IntegerArgument(
            arguments, "--war3-auto-skirmish-map-rows=", 160, 96, 512);
        var asset = War3MapCodec.CreateNew(
            $"auto-skirmish-stress-{columns}x{rows}",
            $"Automated Skirmish Stress {columns}x{rows}",
            columns,
            rows,
            CellSize);
        asset.Metadata.Author = "Automated stress harness";
        asset.Metadata.Description =
            "Ephemeral large battlefield for automated performance tests.";
        asset.Objects.Clear();

        var width = columns * CellSize;
        var height = rows * CellSize;
        var player = new System.Numerics.Vector2(1_200f, height * 0.5f);
        var enemy = new System.Numerics.Vector2(width - 1_200f, height * 0.5f);
        AddSpawn(asset, "spawn-1", 1, player);
        AddSpawn(asset, "spawn-2", 2, enemy);
        AddBaseResources(asset, player, 1f, 1);
        AddBaseResources(asset, enemy, -1f, 2);

        AddGold(asset, "neutral-gold-top", 0,
            new System.Numerics.Vector2(width * 0.5f, height * 0.22f), 80_000);
        AddGold(asset, "neutral-gold-bottom", 0,
            new System.Numerics.Vector2(width * 0.5f, height * 0.78f), 80_000);
        AddNeutralWoodline(asset, width, height, upper: true);
        AddNeutralWoodline(asset, width, height, upper: false);

        if (!War3MapCodec.TryExpand(
                asset, out var runtime, out var validation) || runtime is null)
        {
            throw new InvalidOperationException(
                $"Generated automated stress map is invalid: {validation.Summary}");
        }
        return runtime;
    }

    private static void AddBaseResources(
        War3MapAsset asset,
        System.Numerics.Vector2 home,
        float direction,
        int owner)
    {
        AddGold(asset, $"base-gold-{owner}", owner,
            home + new System.Numerics.Vector2(direction * 235f, 0f), 250_000);
        for (var index = 0; index < 16; index++)
        {
            var row = index / 8;
            var column = index % 8;
            var position = home + new System.Numerics.Vector2(
                direction * ((column - 3.5f) * 46f),
                (row == 0 ? -1f : 1f) * (310f + column % 2 * 18f));
            AddTree(asset, $"base-tree-{owner}-{index}", owner, position);
        }
    }

    private static void AddNeutralWoodline(
        War3MapAsset asset,
        float width,
        float height,
        bool upper)
    {
        var y = height * (upper ? 0.08f : 0.92f);
        var prefix = upper ? "top" : "bottom";
        for (var index = 0; index < 32; index++)
        {
            var x = width * 0.25f + index * width * 0.5f / 31f;
            AddTree(asset, $"neutral-tree-{prefix}-{index}", 0,
                new System.Numerics.Vector2(x, y + (index % 2) * 24f));
        }
    }

    private static void AddSpawn(
        War3MapAsset asset,
        string id,
        int owner,
        System.Numerics.Vector2 position) => asset.Objects.Add(new War3MapObject
    {
        Id = id,
        Kind = War3MapObjectKind.SpawnPoint,
        X = position.X,
        Y = position.Y,
        RadiusX = 32f,
        RadiusY = 32f,
        OwnerSlot = owner,
        Prototype = "human_start"
    });

    private static void AddGold(
        War3MapAsset asset,
        string id,
        int owner,
        System.Numerics.Vector2 position,
        int amount) => asset.Objects.Add(new War3MapObject
    {
        Id = id,
        Kind = War3MapObjectKind.GoldMine,
        X = position.X,
        Y = position.Y,
        RadiusX = 52f,
        RadiusY = 42f,
        OwnerSlot = owner,
        Amount = amount,
        Prototype = "war3_gold_mine"
    });

    private static void AddTree(
        War3MapAsset asset,
        string id,
        int owner,
        System.Numerics.Vector2 position) => asset.Objects.Add(new War3MapObject
    {
        Id = id,
        Kind = War3MapObjectKind.Tree,
        X = position.X,
        Y = position.Y,
        RadiusX = 13f,
        RadiusY = 13f,
        OwnerSlot = owner,
        Amount = War3HumanScenario.TreeHealth,
        Prototype = "lordaeron_tree"
    });

    private static int IntegerArgument(
        string[] arguments,
        string prefix,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = arguments.FirstOrDefault(argument => argument.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase));
        return value is not null &&
               int.TryParse(value[prefix.Length..], out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }
}
