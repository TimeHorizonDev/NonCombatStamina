using UnityEngine;

public static class PlayerExtensions
{
	/// <summary>
	/// True when stamina costs should be skipped for this player: the feature is enabled, no combat signal is active
	/// right now, and the combat grace window has expired.
	/// </summary>
	public static bool IsStaminaUnlimited(this Player __instance)
	{
		if (!NonCombatStamina._isStaminaUnlimited.Value)
			return false;

		if (CombatTracker.RefreshSignals(__instance))
			return false;

		return CombatTracker.IsGraceExpired() && !__instance.IsFlying();
	}
}

/// <summary>
/// Tracks when the local player was last in combat. Any combat signal restarts a grace window of
/// Combat Grace Seconds; stamina is only unlimited once that window has expired.
/// </summary>
public static class CombatTracker
{
	private static float s_combatUntil = float.MinValue;

	/// <summary>
	/// Restart the combat grace window from now.
	/// </summary>
	public static void MarkCombat()
	{
		s_combatUntil = Time.time + Mathf.Max(0f, NonCombatStamina._combatGraceSeconds.Value);
	}

	/// <summary>
	/// Check the continuous combat signals (sensed, targeted, blocking, drawing a bow). If any is active, restart the
	/// grace window and return true.
	/// </summary>
	public static bool RefreshSignals(Player player)
	{
		if (player.IsSensed() || player.IsTargeted() || player.IsBlocking() || player.IsDrawingBow())
		{
			MarkCombat();
			return true;
		}

		return false;
	}

	public static bool IsGraceExpired()
	{
		return Time.time > s_combatUntil;
	}
}