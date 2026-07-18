namespace RtsDemo.Simulation;

/// <summary>Canonical binary codec shared by content containers.</summary>
internal static class CombatProfileBinary
{
    public static void WriteWeapon(
        BinaryWriter writer, in CombatWeaponProfileSnapshot value)
    {
        writer.Write(value.Slot);
        writer.Write((byte)value.TargetLayers);
        writer.Write(value.EnabledByDefault);
        writer.Write(value.RequiredTechnologyId);
        writer.Write(value.AttackDamage);
        writer.Write(value.AttackRange);
        writer.Write(value.AttackCooldownSeconds);
        writer.Write(value.AttackWindupSeconds);
        writer.Write((byte)value.Positioning);
        writer.Write(value.AttacksPerVolley);
        writer.Write((ushort)value.BonusVs);
        writer.Write(value.BonusDamage);
        writer.Write(value.BaseUpgradeDamage);
        writer.Write(value.BonusUpgradeDamage);
        writer.Write(value.ProjectileSpeed);
        writer.Write(value.CanMoveDuringWindup);
        writer.Write(value.CanMoveDuringCooldown);
        writer.Write((byte)value.AttackType);
        writer.Write(value.DamageUpgradeTechnologyId);
        writer.Write(value.MinimumRange);
        writer.Write(value.Area.FullDamageRadius);
        writer.Write(value.Area.HalfDamageRadius);
        writer.Write(value.Area.QuarterDamageRadius);
        writer.Write((byte)value.Area.TargetLayers);
        writer.Write((byte)value.Propagation.Kind);
        writer.Write(value.Propagation.LineDistance);
        writer.Write(value.Propagation.Radius);
        writer.Write(value.Propagation.DamageLossFactor);
        writer.Write(value.Propagation.MaximumTargets);
        writer.Write((byte)value.Propagation.TargetLayers);
        writer.Write(value.Propagation.DistanceUpgradeTechnologyId);
        writer.Write(value.Propagation.DistanceUpgradePerLevel);
    }

    public static CombatWeaponProfileSnapshot ReadWeapon(BinaryReader reader) =>
        new(
            reader.ReadInt32(), (CombatTargetLayer)reader.ReadByte(),
            reader.ReadBoolean(), reader.ReadInt32(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            (CombatPositioningKind)reader.ReadByte(), reader.ReadInt32(),
            (CombatAttribute)reader.ReadUInt16(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadBoolean(), reader.ReadBoolean(),
            (CombatAttackType)reader.ReadByte(), reader.ReadInt32(),
            reader.ReadSingle(),
            new CombatWeaponAreaSnapshot(
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                (CombatTargetLayer)reader.ReadByte()),
            new CombatWeaponPropagationSnapshot(
                (CombatWeaponPropagationKind)reader.ReadByte(),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadInt32(), (CombatTargetLayer)reader.ReadByte(),
                reader.ReadInt32(), reader.ReadSingle()));

    public static void WriteBuildingOnHit(
        BinaryWriter writer, in BuildingWeaponOnHitEffectSnapshot value)
    {
        writer.Write(value.WeaponSlot);
        writer.Write((byte)value.Kind);
        writer.Write(value.UnitMaximumValue);
        writer.Write(value.UnitDamagePerValue);
        writer.Write(value.HeroMaximumValue);
        writer.Write(value.HeroDamagePerValue);
        writer.Write(value.SummonedDamage);
    }

    public static BuildingWeaponOnHitEffectSnapshot ReadBuildingOnHit(
        BinaryReader reader) => new(
        reader.ReadInt32(),
        (BuildingWeaponOnHitEffectKind)reader.ReadByte(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle());
}
