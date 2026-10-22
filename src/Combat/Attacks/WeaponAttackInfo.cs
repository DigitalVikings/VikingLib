using System;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// Runtime metadata associated with an ongoing attack or HitData instance.
    /// Tracks which attack step was executed and references the weapon's profile.
    /// </summary>
    public class WeaponAttackInfo
    {
        /// <summary>
        /// Key identifying the specific attack step (e.g. "primary_1", "primary_3", "secondary").
        /// </summary>
        public string StepKey { get; set; } = "primary_1";

        /// <summary>
        /// Human-readable label for telemetry and debugging (e.g. "Primary Hit 1", "Combo Finisher (Hit 3/3)").
        /// </summary>
        public string DisplayName { get; set; } = "Primary Hit 1";

        /// <summary>
        /// The weapon item data used to perform the attack, if available.
        /// </summary>
        public ItemDrop.ItemData? Weapon { get; set; }

        /// <summary>
        /// The profile dictating multipliers for this weapon.
        /// </summary>
        public WeaponAttackProfile Profile { get; set; }

        /// <summary>
        /// Total number of targets hit by this attack swing.
        /// </summary>
        public int TargetCount { get; set; } = 1;

        /// <summary>
        /// 0-indexed position of this target in the attack's chronological blade sweep order.
        /// </summary>
        public int TargetIndex { get; set; } = 0;

        /// <summary>
        /// Indicates whether this hit is part of a pickaxe swing striking multiple ore chunks.
        /// </summary>
        public bool IsPickaxeSpecial { get; set; } = false;

        /// <summary>
        /// The total number of rock/ore chunks struck by this pickaxe swing.
        /// </summary>
        public int PickaxeChunkCount { get; set; } = 1;

        /// <summary>
        /// Extensible 32-bit bitmask for custom combat flags or proc conditions.
        /// </summary>
        public uint CustomFlags { get; set; } = 0;

        /// <summary>
        /// Combat flags carried by this attack (convenience accessor for CustomFlags mapped to DamageFlags).
        /// </summary>
        public DamageFlags Flags
        {
            get => (DamageFlags)CustomFlags;
            set => CustomFlags = (uint)value;
        }

        public WeaponAttackInfo(string stepKey, string displayName, ItemDrop.ItemData? weapon = null, WeaponAttackProfile? profile = null)
        {
            StepKey = stepKey;
            DisplayName = displayName;
            Weapon = weapon;
            Profile = profile ?? new WeaponAttackProfile();
        }

        /// <summary>
        /// Creates a memberwise copy of this attack metadata.
        /// </summary>
        public WeaponAttackInfo Clone()
        {
            return new WeaponAttackInfo(StepKey, DisplayName, Weapon, Profile)
            {
                TargetCount = TargetCount,
                TargetIndex = TargetIndex,
                IsPickaxeSpecial = IsPickaxeSpecial,
                PickaxeChunkCount = PickaxeChunkCount,
                CustomFlags = CustomFlags
            };
        }

        /// <summary>
        /// Serializes VikingLib hit metadata into a ZPackage stream.
        /// </summary>
        public void Serialize(ZPackage pkg)
        {
            pkg.Write((short)TargetIndex);
            pkg.Write((short)TargetCount);
            pkg.Write(IsPickaxeSpecial);
            pkg.Write((short)PickaxeChunkCount);
            pkg.Write(CustomFlags);
            pkg.Write(StepKey ?? "");
            pkg.Write(DisplayName ?? "");
        }

        /// <summary>
        /// Deserializes VikingLib hit metadata from a ZPackage stream and resolves weapon profile.
        /// </summary>
        public static WeaponAttackInfo Deserialize(ZPackage pkg, byte version, HitData? hit)
        {
            short targetIndex = pkg.ReadShort();
            short targetCount = pkg.ReadShort();
            bool isPickaxeSpecial = pkg.ReadBool();
            short pickaxeChunkCount = pkg.ReadShort();
            uint customFlags = pkg.ReadUInt();
            string stepKey = pkg.ReadString();
            string displayName = pkg.ReadString();

            var attacker = hit?.GetAttacker() as Humanoid;
            var weapon = attacker != null ? attacker.GetCurrentWeapon() : null;
            var profile = WeaponAttackRegistry.GetProfile(weapon);

            return new WeaponAttackInfo(stepKey, displayName, weapon, profile)
            {
                TargetIndex = targetIndex,
                TargetCount = targetCount,
                IsPickaxeSpecial = isPickaxeSpecial,
                PickaxeChunkCount = pickaxeChunkCount,
                CustomFlags = customFlags
            };
        }
    }
}
