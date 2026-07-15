namespace RtsDemo.Presentation;

public enum TerrainShowcaseTarget
{
    PresetGallery,
    Traversal,
    DynamicTopology,
    VisionCombat,
    AuthoringWorkspace
}

public sealed record TerrainShowcaseEntry(
    TerrainShowcaseTarget Target,
    string Title,
    string Scope,
    string Summary,
    string Evidence);

/// <summary>
/// Presentation metadata for the dedicated terrain test page. The entries do
/// not load scenes or depend on terrain runtime types.
/// </summary>
public static class TerrainShowcaseCatalog
{
    private static readonly TerrainShowcaseEntry[] Values =
    [
        new(
            TerrainShowcaseTarget.PresetGallery,
            "12 张地形预制总览",
            "资产覆盖",
            "连续浏览开放地、宽窄坡、环形高地、低地盆地、浅滩、陆桥、山脊与大型四路线地图。",
            "12 张独立资产 · 21 条坡道 · 四个坡道方向"),
        new(
            TerrainShowcaseTarget.Traversal,
            "跨层移动与坡道穿越",
            "寻路",
            "观察单位从低地进入坡道、切换高度层并在高地继续移动，直接检查跨层路线和抵达表现。",
            "真实 TerrainMap + ClearanceBake + 坡道拓扑"),
        new(
            TerrainShowcaseTarget.DynamicTopology,
            "建筑封路与动态改道",
            "建造兼容",
            "在平行坡道附近放置和移除建筑，检查可建造区域、路线封锁、备用路线与拆除后的恢复。",
            "动态建筑阻挡 · 连通性保护 · 路线重算"),
        new(
            TerrainShowcaseTarget.VisionCombat,
            "高低地视野与战斗",
            "玩法联动",
            "让交战单位穿越高地边缘，检查高地遮挡、坡道视野、显隐变化以及战斗目标是否合法。",
            "高地视野 · 坡道暴露 · 战斗目标过滤"),
        new(
            TerrainShowcaseTarget.AuthoringWorkspace,
            "地形编辑与运行时导出",
            "编辑工作流",
            "使用独立工作区修改高度、通行、建造、水域和坡道，再验证并导出可供运行时加载的地形资产。",
            "编辑数据 · 明确校验 · .tres 运行时资产")
    ];

    public static IReadOnlyList<TerrainShowcaseEntry> Entries => Values;
}
