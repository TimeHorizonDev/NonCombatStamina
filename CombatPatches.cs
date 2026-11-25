using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

/// <summary>
/// Refresh the combat window every physics tick for the local player, so sensed/targeted/blocking/drawing
/// restart the grace window even when no stamina is being spent.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.UpdateStats), new[] { typeof(float) })]
public static class CombatSignals_Patch
{
	[HarmonyPostfix]
	public static void Postfix(Player __instance)
	{
		if (ReferenceEquals(__instance, Player.m_localPlayer))
			CombatTracker.RefreshSignals(__instance);
	}
}

/// <summary>
/// Taking damage counts as combat: enemy and player hits, fire, poison, smoke, lava, drowning, freezing and other
/// environmental damage. Fall damage does not. ApplyDamage runs on the owner for both RPC hits and status effect ticks.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
public static class DamageTaken_Patch
{
	[HarmonyPrefix]
	public static void Prefix(Character __instance, HitData hit)
	{
		if (hit != null && hit.m_hitType != HitData.HitType.Fall && ReferenceEquals(__instance, Player.m_localPlayer) && hit.GetTotalDamage() > 0f)
			CombatTracker.MarkCombat();
	}
}

/// <summary>
/// Dealing damage to another character counts as combat. Character.Damage runs on the attacker's client
/// (melee, projectiles and AoE) before the hit is sent to the target's owner.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
public static class DamageDealt_Patch
{
	[HarmonyPrefix]
	public static void Prefix(Character __instance, HitData hit)
	{
		Player local = Player.m_localPlayer;
		if (hit == null || (object) local == null || ReferenceEquals(__instance, local) || hit.m_attacker.IsNone())
			return;

		if (hit.m_attacker == local.GetZDOID())
			CombatTracker.MarkCombat();
	}
}

/// <summary>
/// Remember whether the local player was crouched when the attack started. The attack animation replaces the
/// crouch animation, so Player.IsCrouching() is no longer reliable once the attack is running.
/// </summary>
[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
public static class StartAttack_Patch
{
	internal static bool s_startedCrouched;

	[HarmonyPrefix]
	public static void Prefix(Humanoid __instance)
	{
		if (ReferenceEquals(__instance, Player.m_localPlayer))
			s_startedCrouched = __instance.IsCrouching();
	}
}

/// <summary>
/// Replacement for the attack-start Character.UseStamina call in Attack.Update.
/// </summary>
public static class AttackStamina
{
	private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> s_weapon = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>(nameof(Attack.m_weapon));

	public static void UseAttackStamina(Character character, float stamina, Attack attack)
	{
		if (character is Player player && ReferenceEquals(player, Player.m_localPlayer) && IsFreeSwing(player, attack))
			return;

		character.UseStamina(stamina);
	}

	private static bool IsFreeSwing(Player player, Attack attack)
	{
		// Sneak attacks always cost vanilla stamina.
		if (StartAttack_Patch.s_startedCrouched || player.IsCrouching())
			return false;

		// Only allow free swings for axes and pickaxes
		if ( !IsToolSwing(attack))
			return false;

		return NonCombatStamina._freeStaminaWhileUsingTools.Value && player.IsStaminaUnlimited();
	}

	private static bool IsToolSwing(Attack attack)
	{
		ItemDrop.ItemData weapon = attack != null ? s_weapon(attack) : null;
		if (weapon == null || weapon.m_shared == null)
			return false;

		Skills.SkillType skill = weapon.m_shared.m_skillType;
		return skill == Skills.SkillType.Axes || skill == Skills.SkillType.Pickaxes;
	}
}

/// <summary>
/// Free mount run stamina out of combat when the local player is the rider. Swaps the run-drain
/// Sadle.UseStamina call in Sadle.UpdateRiding (the one after the m_runStaminaDrain load) for MountStamina.UseRunStamina.
/// Swimming drain on the mount is unchanged.
/// </summary>
[HarmonyPatch(typeof(Sadle), nameof(Sadle.UpdateRiding))]
public static class SadleUpdateRiding_Patch
{
	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> MountRunStaminaUse(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> codes = instructions.ToList();
		FieldInfo runDrain = AccessTools.Field(typeof(Sadle), nameof(Sadle.m_runStaminaDrain));
		MethodInfo useStamina = AccessTools.Method(typeof(Sadle), nameof(Sadle.UseStamina), new[] { typeof(float) });
		MethodInfo helper = AccessTools.Method(typeof(MountStamina), nameof(MountStamina.UseRunStamina), new[] { typeof(Sadle), typeof(float) });

		int start = codes.FindIndex(c => c.opcode == OpCodes.Ldfld && Equals(c.operand, runDrain));
		if (start >= 0 && useStamina != null)
		{
			for (int i = start + 1; i < codes.Count; i++)
			{
				if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt) && Equals(codes[i].operand, useStamina))
				{
					codes[i].opcode = OpCodes.Call;
					codes[i].operand = helper;
					return codes;
				}
			}
		}

		NonCombatStamina.ModLogger.LogWarning("Compatibility: Could not find the mount run stamina call in Sadle.UpdateRiding. Patch not applied.");
		return codes;
	}
}

/// <summary>
/// Helpers for the mount patch. Kept in their own class so the private-method delegates are only created
/// after the transpiler has found the call.
/// </summary>
public static class MountStamina
{
	private static readonly Action<Sadle, float> s_useStamina = AccessTools.MethodDelegate<Action<Sadle, float>>(AccessTools.Method(typeof(Sadle), nameof(Sadle.UseStamina), new[] { typeof(float) }));
	private static readonly Func<Sadle, bool> s_isLocalUser = AccessTools.MethodDelegate<Func<Sadle, bool>>(AccessTools.Method(typeof(Sadle), nameof(Sadle.IsLocalUser)));

	public static void UseRunStamina(Sadle sadle, float stamina)
	{
		Player local = Player.m_localPlayer;
		if ((object) local != null && s_isLocalUser(sadle) && NonCombatStamina._freeStaminaWhileRiding.Value && local.IsStaminaUnlimited())
			return;

		s_useStamina(sadle, stamina);
	}
}