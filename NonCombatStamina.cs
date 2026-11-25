using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

//#pragma warning disable CS8618

internal static class PluginDetails
{
	public const string AUTHOR = "TimeHorizon";
	public const string NAME = "NonCombatStamina";
	public const string GUID = AUTHOR + "." + NAME;
	public const string VERSION = "1.0.1";
	// Oldest version that can join a server running this version (and vice versa). 1.0.1 is the first
	// version with ServerSync. Raise it only when a release changes what the synced settings mean.
	public const string MINIMUM_REQUIRED_VERSION = "1.0.1";
	public const string DESC = "Unlimited stamina out of combat for running, jumping, sneaking, riding your mount, and using tools.";
}

[BepInPlugin(PluginDetails.GUID, PluginDetails.NAME, PluginDetails.VERSION)]
public class NonCombatStamina : BaseUnityPlugin
{
	public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(PluginDetails.NAME);
	public static bool IsDedicatedServer => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

	// ServerSync (merged into this DLL by ILRepack). The name is the sync channel and must never change.
	// ModRequired is false, so vanilla clients can still join a server that runs this mod, and a client
	// with this mod can still join a vanilla server (its own config applies there).
	internal static readonly ConfigSync ConfigSync = new ConfigSync(PluginDetails.GUID)
	{
		DisplayName = PluginDetails.NAME,
		CurrentVersion = PluginDetails.VERSION,
		MinimumRequiredVersion = PluginDetails.MINIMUM_REQUIRED_VERSION,
		ModRequired = false
	};

	public enum ToleranceLevel
	{
		Default = 2,
		Loose = 3,
		Looser = 4,
		Loosest = 5
	}

	private static ConfigEntry<bool> _lockConfiguration;
	internal static ConfigEntry<bool> _isStaminaUnlimited;
	private static ConfigEntry<ToleranceLevel> _toleranceLevel;
	internal static ConfigEntry<float> _combatGraceSeconds;
	internal static ConfigEntry<bool> _freeStaminaWhileRunning;
	internal static ConfigEntry<bool> _freeStaminaWhileSneaking;
	internal static ConfigEntry<bool> _freeStaminaWhileJumping;
	internal static ConfigEntry<bool> _freeStaminaWhileEncumbered;
	internal static ConfigEntry<bool> _freeStaminaWhileSwimming;
	internal static ConfigEntry<bool> _freeStaminaWhileFloating;
	internal static ConfigEntry<bool> _freeStaminaWhileRiding;
	internal static ConfigEntry<bool> _freeStaminaWhileUsingTools;
	internal static ConfigEntry<bool> _freeStaminaWhileBuilding;
	internal const float VanillaGroundTolerance = 0.2f;
	internal static float _tolerance = VanillaGroundTolerance;

	[HarmonyPatch(typeof(Attack), nameof(Attack.Update))]
	public static class AttackUpdate_Patch
	{
		/// <summary>
		/// When not in combat, don't use stamina for mining and chopping (Axes and Pickaxes skill
		/// swings that aren't sneak attacks). Swaps "callvirt UseStamina" for AttackStamina.UseAttackStamina,
		/// passing the Attack instance as an extra argument so the helper can check the weapon.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> AttackStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			List<CodeInstruction> codes = instructions.ToList();
			MethodInfo helper = AccessTools.Method(typeof(AttackStamina), nameof(AttackStamina.UseAttackStamina), new[] { typeof(Character), typeof(float), typeof(Attack) });

			for (int i = 0; i < codes.Count; i++)
			{
				if (IsUseStaminaCall(codes[i]))
				{
					// Stack before the call is (character, stamina). Push 'this' (the Attack) and call the 3-arg helper.
					// A label on the call itself would make the inserted ldarg.0 unreachable from that branch, so bail out.
					if (codes[i].labels.Count > 0 || codes[i].blocks.Count > 0)
						break;

					codes[i].opcode = OpCodes.Call;
					codes[i].operand = helper;
					codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
					return codes;
				}
			}

			ModLogger.LogWarning($"Compatibility: Could not patch the UseStamina call in {original?.DeclaringType?.Name}.{original?.Name}. Patch not applied.");
			return codes;
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.OnSneaking))]
	public static class OnSneaking_Patch
	{
		/// <summary>
		/// Patch the OnSneaking method to call bool PlayerExtensions.IsStaminaUnlimited() to see if the player currently has
		/// unlimited stamina. Vanilla behavior reduces movement speed and creates a stamina cost. Both add a time cost to
		/// the action, with the stamina cost compromising combat readiness, a system which favors frustration over challenge.
		/// Frustration adds churn and is unnecessary here.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> SneakStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseSneakStamina));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.OnJump))]
	public static class OnJump_Patch
	{
		/// <summary>
		/// Patch the OnJump method to call bool PlayerExtensions.IsStaminaUnlimited() to see if the player currently has
		/// unlimited stamina. Vanilla forces players to consume stamina during climbing, adding a time cost for players
		/// to frequently stop movement to recover. Systems that force players to stop playing for frequent rests are
		/// annoying. We retain the cost while in active combat, but no need for artifical time costs outside of that.
		/// It's not like there is a cleverly designed puzzle that climbers must navigate with a limited resource. That
		/// mini-game might be interesting, but is not really present with the current design. This does remove the
		/// stamina gate for larger mountains, but other gates already exist, so don't see the point in blocking the
		/// movement.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> JumpStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseJumpStamina));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.CheckRun))]
	public static class CheckRun_Patch
	{
		/// <summary>
		/// Allow unlimited stamina while running.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> RunStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseRunStamina));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.UpdateStats), new[] { typeof(float) })]
	public static class UpdateStats_Patch
	{
		/// <summary>
		/// Allow unlimited stamina while encumbered.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> EncumberedStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseEncumberedStamina));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.OnSwimming))]
	public static class OnSwimming_Patch
	{
		/// <summary>
		/// Allow unlimited stamina while swimming when that option is on. Off by default, so swimming
		/// uses vanilla stamina unless the setting is enabled.
		/// </summary>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> SwimStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseSwimStamina));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
	public static class UpdatePlacement_Patch
	{
		/// <summary>
		/// Skip build stamina for placing and removing pieces when Free Stamina While Building is on.
		/// UpdatePlacement has two UseStamina(GetBuildStamina()) calls (remove, then place).
		/// </summary>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> BuildStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			return ReplaceUseStaminaCall(instructions, original, nameof(UseBuildStamina), replaceAll: true);
		}
	}

	[HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.UpdateStatusEffect))]
	public static class SEStatsUpdate_Patch
	{
		/// <summary>
		/// When a slow-fall status effect charges m_staminaDrainPerSec, route that call through
		/// UseSlowFallStamina so Free Stamina While Floating can skip it. The vanilla Feather Cape
		/// currently has that drain at 0.
		/// </summary>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> SlowFallStaminaUse(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			List<CodeInstruction> codes = instructions.ToList();
			MethodInfo helper = AccessTools.Method(typeof(NonCombatStamina), nameof(UseSlowFallStamina), new[] { typeof(Character), typeof(float), typeof(SE_Stats) });

			for (int i = 0; i < codes.Count; i++)
			{
				if (IsUseStaminaCall(codes[i]))
				{
					if (codes[i].labels.Count > 0 || codes[i].blocks.Count > 0)
						break;

					codes[i].opcode = OpCodes.Call;
					codes[i].operand = helper;
					codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
					return codes;
				}
			}

			ModLogger.LogWarning($"Compatibility: Could not patch the UseStamina call in {original?.DeclaringType?.Name}.{original?.Name}. Patch not applied.");
			return codes;
		}
	}

	/// <summary>
	/// Replace the first Character.UseStamina(float) call in the patched method with a call to the
	/// named helper. Both consume the same stack (instance, amount) and return void, so the surrounding
	/// IL, its labels and exception blocks are left untouched.
	/// If the call can't be found, the original code is returned unchanged and a warning is logged.
	/// </summary>
	/// <param name="instructions">List of instructions for the method to be patched.</param>
	/// <param name="original">The method being patched, used for logging.</param>
	/// <returns></returns>
	public static IEnumerable<CodeInstruction> ReplaceUseStaminaCall(IEnumerable<CodeInstruction> instructions, MethodBase original, string helperName, bool replaceAll = false)
	{
		List<CodeInstruction> codes = instructions.ToList();
		MethodInfo helper = AccessTools.Method(typeof(NonCombatStamina), helperName, new[] { typeof(Character), typeof(float) });
		int replaced = 0;

		for (int i = 0; i < codes.Count; i++)
		{
			if (IsUseStaminaCall(codes[i]))
			{
				codes[i].opcode = OpCodes.Call;
				codes[i].operand = helper;
				replaced++;
				if (!replaceAll)
					return codes;
			}
		}

		if (replaced == 0)
			ModLogger.LogWarning($"Compatibility: Could not find a UseStamina call in {original?.DeclaringType?.Name}.{original?.Name}. Patch not applied.");

		return codes;
	}

	/// <summary>
	/// Matches a virtual call to Character.UseStamina(float) or its Player override.
	/// </summary>
	internal static bool IsUseStaminaCall(CodeInstruction instruction)
	{
		if (instruction.opcode != OpCodes.Callvirt || !(instruction.operand is MethodInfo method))
			return false;

		if (method.Name != nameof(Character.UseStamina) || (method.DeclaringType != typeof(Character) && method.DeclaringType != typeof(Player)))
			return false;

		ParameterInfo[] parameters = method.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType == typeof(float);
	}

	/// <summary>
	/// Replacement for Character.UseStamina(float) at the patched call sites. Skips the stamina charge only
	/// when the action toggle is on, the character is the local player, and they currently have unlimited
	/// stamina (out of combat and the master setting is enabled). Otherwise charges stamina as vanilla would.
	/// </summary>
	internal static void UseStaminaUnlessFree(Character character, float stamina, ConfigEntry<bool> enabled)
	{
		if (enabled.Value && character is Player player && ReferenceEquals(player, Player.m_localPlayer) && player.IsStaminaUnlimited())
			return;

		character.UseStamina(stamina);
	}

	public static void UseRunStamina(Character character, float stamina)
	{
		if (!(character is Player player) || !ReferenceEquals(player, Player.m_localPlayer) || !player.IsStaminaUnlimited())
		{
			character.UseStamina(stamina);
			return;
		}

		ConfigEntry<bool> toggle = IsFloating(character) ? _freeStaminaWhileFloating : _freeStaminaWhileRunning;
		if (toggle.Value)
			return;

		character.UseStamina(stamina);
	}

	public static void UseSneakStamina(Character character, float stamina)
	{
		UseStaminaUnlessFree(character, stamina, _freeStaminaWhileSneaking);
	}

	public static void UseJumpStamina(Character character, float stamina)
	{
		UseStaminaUnlessFree(character, stamina, _freeStaminaWhileJumping);
	}

	public static void UseEncumberedStamina(Character character, float stamina)
	{
		UseStaminaUnlessFree(character, stamina, _freeStaminaWhileEncumbered);
	}

	public static void UseSwimStamina(Character character, float stamina)
	{
		UseStaminaUnlessFree(character, stamina, _freeStaminaWhileSwimming);
	}

	public static void UseBuildStamina(Character character, float stamina)
	{
		UseStaminaUnlessFree(character, stamina, _freeStaminaWhileBuilding);
	}

	/// <summary>
	/// True when the character is airborne with a slow-fall / Feather fall effect
	/// (a status effect whose m_maxMaxFallSpeed caps downward speed, such as the Feather Cape).
	/// </summary>
	internal static bool IsFloating(Character character)
	{
		if (character.IsOnGround() || character.IsSwimming())
			return false;

		SEMan seman = character.GetSEMan();
		if (seman == null)
			return false;

		List<StatusEffect> effects = seman.GetStatusEffects();
		if (effects == null)
			return false;

		for (int i = 0; i < effects.Count; i++)
		{
			if (effects[i] is SE_Stats stats && stats.m_maxMaxFallSpeed > 0f)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Replacement for Character.UseStamina(float) in SE_Stats.UpdateStatusEffect.
	/// Skips a slow-fall effect's m_staminaDrainPerSec charge when Free Stamina While Floating
	/// is on. Vanilla Feather Cape leaves that drain at 0; this covers other or future effects.
	/// </summary>
	public static void UseSlowFallStamina(Character character, float stamina, SE_Stats stats)
	{
		if (stats != null && stats.m_maxMaxFallSpeed > 0f)
		{
			UseStaminaUnlessFree(character, stamina, _freeStaminaWhileFloating);
			return;
		}

		character.UseStamina(stamina);
	}

	private static readonly AccessTools.FieldRef<Character, float> s_lastGroundTouch = AccessTools.FieldRefAccess<Character, float>(nameof(Character.m_lastGroundTouch));

	/// <summary>
	/// Ground check used by the patched Character.Jump. Vanilla IsOnGround() is always honored. For the local
	/// player only, a jump is also allowed while the time since the last ground contact is within the configured
	/// tolerance (jump-only coyote time). A jump sets m_lastGroundTouch to 1, so the window is spent once per jump.
	/// </summary>
	public static bool IsOnGroundForJump(Character character)
	{
		if (character.IsOnGround())
			return true;

		return ReferenceEquals(character, Player.m_localPlayer) && s_lastGroundTouch(character) < _tolerance;
	}

	[HarmonyPatch(typeof(Character), nameof(Character.Jump))]
	public static class Jump_Patch
	{
		/// <summary>
		/// The ability to Jump intermittently fails while running downhill or over small rises, such as
		/// at the coastline. The character briefly pops into the air, and vanilla IsOnGround() only allows
		/// 0.2 seconds since the last ground contact, so the jump is rejected.
		/// 
		/// Rather than loosening IsOnGround() everywhere (which also changes air control, friction, dodge,
		/// fall tracking and more), this replaces only the IsOnGround() call inside Character.Jump with
		/// IsOnGroundForJump(Character), giving the local player a short coyote-time window for jumps.
		/// </summary>
		/// <param name="instructions">List of instructions for the method to be patched.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> JumpGroundCheck(IEnumerable<CodeInstruction> instructions)
		{
			// Swap "call Character::IsOnGround()" for "call IsOnGroundForJump(Character)". Both consume the
			// instance and push a bool, so the surrounding IL and its labels are unchanged.
			List<CodeInstruction> codes = instructions.ToList();
			MethodInfo isOnGround = AccessTools.Method(typeof(Character), nameof(Character.IsOnGround), Type.EmptyTypes);
			MethodInfo helper = AccessTools.Method(typeof(NonCombatStamina), nameof(IsOnGroundForJump), new[] { typeof(Character) });

			for (int i = 0; i < codes.Count; i++)
			{
				if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt) && Equals(codes[i].operand, isOnGround))
				{
					codes[i].opcode = OpCodes.Call;
					codes[i].operand = helper;
					return codes;
				}
			}

			ModLogger.LogWarning("Compatibility: Could not find the IsOnGround call in Character.Jump. Jump tolerance not applied.");
			return codes;
		}
	}

	private readonly Harmony _harmony = new Harmony(PluginDetails.GUID);
	public void Awake()
	{
		// Config and ServerSync are set up on clients and dedicated servers alike, so a server running the
		// mod can send (and, when locked, enforce) its values. Gameplay patches are client-only.
		_lockConfiguration = Config.Bind("General", "Lock Configuration", true, "Set on the server. When on, the server's values for the synced settings are enforced on every player and only server admins can change them. When off, players can change the synced settings in game and the change applies to everyone on the server. Has no effect on servers without this mod.");
		ConfigSync.AddLockingConfigEntry(_lockConfiguration);

		_isStaminaUnlimited = Config.Bind("General", "Enable Unlimited Stamina", true, "Enable unlimited stamina when out of combat. Individual actions are controlled in the Advanced section. Synced with the server.");
		ConfigSync.AddConfigEntry(_isStaminaUnlimited);

		_combatGraceSeconds = Config.Bind("General", "Combat Grace Seconds", 4f, new ConfigDescription("Seconds after the last combat signal before stamina becomes unlimited again. Synced with the server.", new AcceptableValueRange<float>(0f, 60f)));
		ConfigSync.AddConfigEntry(_combatGraceSeconds);

		_toleranceLevel = Config.Bind("General", "Ground Tolerance", ToleranceLevel.Loose, "Allow a jump to trigger after losing ground contact for a short duration. Default = vanilla 0.2s, Loose = 0.3s, Looser = 0.4s, Loosest = 0.5s. Synced with the server.");
		ConfigSync.AddConfigEntry(_toleranceLevel);
		_tolerance = (float) _toleranceLevel.Value / 10f;
		_toleranceLevel.SettingChanged += onChangedTolerance;

		_freeStaminaWhileRunning = Config.Bind("Advanced", "Free Stamina While Running", true, AdvancedSetting("When on, running costs no stamina while out of combat. When off, running uses vanilla stamina. Sprinting while gliding with Feather fall is controlled by Free Stamina While Floating. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileRunning);

		_freeStaminaWhileSneaking = Config.Bind("Advanced", "Free Stamina While Sneaking", true, AdvancedSetting("When on, sneaking costs no stamina while out of combat. When off, sneaking uses vanilla stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileSneaking);

		_freeStaminaWhileJumping = Config.Bind("Advanced", "Free Stamina While Jumping", true, AdvancedSetting("When on, jumping costs no stamina while out of combat. When off, jumping uses vanilla stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileJumping);

		_freeStaminaWhileEncumbered = Config.Bind("Advanced", "Free Stamina While Encumbered", true, AdvancedSetting("When on, moving while encumbered costs no stamina while out of combat. When off, encumbered movement uses vanilla stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileEncumbered);

		_freeStaminaWhileSwimming = Config.Bind("Advanced", "Free Stamina While Swimming", false, AdvancedSetting("When on, swimming costs no stamina while out of combat. When off (the default), swimming uses vanilla stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileSwimming);

		_freeStaminaWhileFloating = Config.Bind("Advanced", "Free Stamina While Floating", false, AdvancedSetting("When on, sprinting while gliding with Feather fall (the Feather Cape) costs no stamina while out of combat. When off (the default), that stamina is charged as in vanilla. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileFloating);

		_freeStaminaWhileRiding = Config.Bind("Advanced", "Free Stamina While Riding", true, AdvancedSetting("When on, running on your mount costs no stamina while you are the rider and out of combat. Mount swimming still uses vanilla stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileRiding);

		_freeStaminaWhileUsingTools = Config.Bind("Advanced", "Free Stamina While Using Tools", true, AdvancedSetting("When on, axe and pickaxe swings cost no stamina while out of combat. Sneak attacks always cost stamina. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileUsingTools);

		_freeStaminaWhileBuilding = Config.Bind("Advanced", "Free Stamina While Building", true, AdvancedSetting("When on, placing and removing pieces costs no stamina while out of combat. Synced with the server."));
		ConfigSync.AddConfigEntry(_freeStaminaWhileBuilding);

		if (IsDedicatedServer)
		{
			ModLogger.LogInfo("Dedicated server: hosting config sync only, gameplay patches not applied.");
			return;
		}

		_harmony.PatchAll(Assembly.GetExecutingAssembly());
	}

	private static ConfigDescription AdvancedSetting(string description)
	{
		return new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdvanced = true });
	}

	private void onChangedTolerance(object sender, EventArgs e)
	{
		// Also fires when ServerSync applies the server's value. IsOnGroundForJump reads _tolerance on
		// every call, so no re-patch is needed.
		_tolerance = (float) _toleranceLevel.Value / 10f;
	}
}

/// <summary>
/// Dummy type used by BepInEx ConfigurationManager. It is discovered by type name, so this
/// project does not need a ConfigurationManager reference. Settings with IsAdvanced set are
/// hidden until the user shows advanced settings.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
	public bool? IsAdvanced;
}
