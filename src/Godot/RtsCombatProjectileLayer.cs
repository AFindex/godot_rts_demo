using Godot;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

public partial class RtsCombatProjectileLayer : Node2D
{
    private CombatPresentationFrame _frame = CombatPresentationFrame.Empty;

    [Export]
    public RtsCombatPresentationThemeResource? Theme { get; set; }

    public RtsCombatProjectileLayer()
    {
        ZIndex = 60;
    }

    public void SetFrame(CombatPresentationFrame frame)
    {
        _frame = frame;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var theme = Theme ?? new RtsCombatPresentationThemeResource();
        foreach (var projectile in _frame.Projectiles)
            DrawProjectile(projectile, theme);
        foreach (var cue in _frame.Cues)
            DrawCue(cue, theme);
    }

    private void DrawProjectile(
        CombatProjectilePresentationSnapshot projectile,
        RtsCombatPresentationThemeResource theme)
    {
        var color = ColorFor(projectile.VisualKind, theme);
        if (projectile.Trail.Length >= 2)
        {
            var points = new Vector2[projectile.Trail.Length];
            for (var index = 0; index < points.Length; index++)
                points[index] = GodotPathProvider.ToGodot(projectile.Trail[index]);
            DrawPolyline(points, color with { A = 0.48f }, theme.TrailWidth,
                antialiased: true);
        }

        var position = GodotPathProvider.ToGodot(projectile.Position);
        var heading = GodotPathProvider.ToGodot(projectile.Heading);
        switch (projectile.VisualKind)
        {
            case CombatProjectileVisualKind.Bolt:
                DrawLine(position - heading * 7f, position + heading * 7f,
                    color, theme.BoltRadius, antialiased: true);
                break;
            case CombatProjectileVisualKind.Orb:
                DrawCircle(position, theme.OrbRadius, color);
                DrawArc(position, theme.OrbRadius + 3f, 0f, MathF.Tau, 16,
                    color with { A = 0.6f }, 1.5f, antialiased: true);
                break;
            case CombatProjectileVisualKind.Volley:
                var radius = theme.VolleyRadius;
                DrawPolyline(
                    [
                        position + new Vector2(0f, -radius),
                        position + new Vector2(radius, 0f),
                        position + new Vector2(0f, radius),
                        position + new Vector2(-radius, 0f),
                        position + new Vector2(0f, -radius)
                    ],
                    color, 2f, antialiased: true);
                break;
        }
    }

    private void DrawCue(
        CombatPresentationCueSnapshot cue,
        RtsCombatPresentationThemeResource theme)
    {
        var position = GodotPathProvider.ToGodot(cue.Position);
        var alpha = 1f - cue.NormalizedAge;
        var radius = Mathf.Lerp(6f, theme.ImpactRadius, cue.NormalizedAge);
        if (cue.Kind == CombatPresentationCueKind.Expired)
        {
            var color = theme.ExpiredColor with { A = alpha };
            DrawLine(position - new Vector2(radius, radius),
                position + new Vector2(radius, radius), color, 2f);
            DrawLine(position - new Vector2(radius, -radius),
                position + new Vector2(radius, -radius), color, 2f);
            return;
        }

        var impactColor = (cue.BonusApplied
            ? theme.BonusImpactColor
            : theme.ImpactColor) with { A = alpha };
        DrawArc(position, radius, 0f, MathF.Tau, 24, impactColor, 2.5f,
            antialiased: true);
        if (cue.Damage > 0f)
        {
            DrawString(ThemeDB.FallbackFont,
                position + new Vector2(8f, -radius - 2f),
                $"-{cue.Damage:0.#}", HorizontalAlignment.Left, -1f, 12,
                impactColor);
        }
    }

    private static Color ColorFor(
        CombatProjectileVisualKind kind,
        RtsCombatPresentationThemeResource theme) => kind switch
    {
        CombatProjectileVisualKind.Orb => theme.OrbColor,
        CombatProjectileVisualKind.Volley => theme.VolleyColor,
        _ => theme.BoltColor
    };
}
