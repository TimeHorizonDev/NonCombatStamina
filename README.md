# NonCombatStamina

This mod retains stamina management as a combat mechanic, but frees up player stamina for other non-combat related actions, such as running, jumping, mining, etc. Stamina usage is also retained for actions that have limits built into the design, such as swimming or floating. However, these can be toggled in advanced options.

Mining or chopping without any creatures nearby would normally drain a player's stamina. Stamina recovery is very slow in vanilla so this results in the player stopping all activity during the recovery period. Sitting there doing nothing in a game is quite boring. Since this forced wait occurs frequently, players end up spending a significant amount of time doing nothing. The side-effect of this design is also that players can be jumped by fresh spawns with no stamina available to fight, creating an edge-case that we find unfair. While this can be managed by forcing yourself to stop all activity more frequently to reserve a small amount of combat stamina, it just isn't fun to play that way.

This mod incorporates existing combat detection mechanics with a grace period and allows for unlimited stamina while out of combat. Note that vanilla still has a spawn system with random elements that can produce non-stop waves of creatures to harass your peaceful mining experience. This mod cannot help with that.

Default configuration also allows for unlimited climbing while out of combat. This can improve the experience in the Mountains and Mistlands biomes. Side effect is that steep mountains can be climbed regardless of stamina pool size. If you prefer to jump from rock to rock with frequent rest periods, this can be re-enabled. The same applies to encumbered movement. There's already a time-cost and risk associated with slower movement, so I've kept that enabled by default.

It also adds a small adjustment for jump tolerance. Running across uneven terrain can produce occassional failures to jump as the player briefly loses contact with the ground. It becomes worse with mods that increase player run speed. Increase the jump tolerance further to reduce the occurance of this issue.

## Features

- **Out-of-combat stamina.** While you are out of combat, these stamina costs are skipped when their setting is on:
  - Running (on by default)
  - Sneaking (on by default)
  - Jumping (on by default)
  - Moving while encumbered (on by default)
  - Running on your mount, when you are the rider (on by default)
  - Using tools (Axe, Pickaxe) to chop or mine (on by default)
  - Placing and removing pieces (on by default)
  - Swimming (off by default)
  - Sprinting while gliding with Feather fall, such as the Feather Cape (off by default)
- **Sneak attacks always cost stamina.** Any attack started while crouched uses vanilla stamina.
- **Combat detection with a grace window.** You count as in combat while any of these is happening, and for `Combat Grace Seconds` (default 4) afterwards:
  - A creature has sensed or targeted you (Valheim's own `IsSensed` / `IsTargeted` checks). This includes fleeing animals such as deer.
  - You are blocking or drawing a bow.
  - You take damage of any kind except fall damage: hits from creatures or players, fire, poison, smoke, Ashlands lava, drowning, freezing and other environmental damage.
  - You damage a creature or another player.
- **Jump coyote time.** When running downhill or over small bumps, the character can leave the ground for a moment and vanilla rejects the jump. The Ground Tolerance setting lets you still jump for a short time after losing ground contact. It only affects the jump check for your own character; every other ground check, and every other character, keeps vanilla behavior.

## Installation

- **Mod manager (recommended):** install with Gale or r2modman. BepInExPack Valheim is required.
- **Manual:** install BepInExPack Valheim, then copy `NonCombatStamina.dll` into `BepInEx/plugins`.
- **Server (optional):** install it on the dedicated server too if you want the server to control the settings. See [Server setup](#server-setup).

## Configuration

The config file is `BepInEx/config/TimeHorizon.NonCombatStamina.cfg`, created on first launch. Settings are in the `[General]` and `[Advanced]` sections and can be changed in game with a configuration manager; changes apply immediately. The Advanced settings are hidden by default in Configuration Manager until you show advanced settings.

| Setting | Default | Synced | Description |
| --- | --- | --- | --- |
| `Lock Configuration` | `true` | Server controls it | Set on the server. When on, the server's values are enforced and only server admins can change them. When off, players can change the synced settings in game and the change applies to everyone on the server. |
| `Enable Unlimited Stamina` | `true` | Yes | Enable unlimited stamina when out of combat. Individual actions are controlled in the Advanced section. |
| `Combat Grace Seconds` | `4` | Yes | Seconds after the last combat signal before stamina becomes unlimited again (0 to 60). |
| `Ground Tolerance` | `Loose` | Yes | Allow a jump to trigger after losing ground contact for a short duration. `Default` = vanilla 0.2 s, `Loose` = 0.3 s, `Looser` = 0.4 s, `Loosest` = 0.5 s. |

`[Advanced]` settings only apply when `Enable Unlimited Stamina` is on and you are out of combat:

| Setting | Default | Synced | Description |
| --- | --- | --- | --- |
| `Free Stamina While Running` | `true` | Yes | When on, running costs no stamina while out of combat. When off, running uses vanilla stamina. Sprinting while gliding with Feather fall is controlled by `Free Stamina While Floating`. |
| `Free Stamina While Sneaking` | `true` | Yes | When on, sneaking costs no stamina while out of combat. When off, sneaking uses vanilla stamina. |
| `Free Stamina While Jumping` | `true` | Yes | When on, jumping costs no stamina while out of combat. When off, jumping uses vanilla stamina. |
| `Free Stamina While Encumbered` | `true` | Yes | When on, moving while encumbered costs no stamina while out of combat. When off, encumbered movement uses vanilla stamina. |
| `Free Stamina While Swimming` | `false` | Yes | When on, swimming costs no stamina while out of combat. When off (the default), swimming uses vanilla stamina. |
| `Free Stamina While Floating` | `false` | Yes | When on, sprinting while gliding with Feather fall (the Feather Cape) costs no stamina while out of combat. When off (the default), that stamina is charged as in vanilla. |
| `Free Stamina While Riding` | `true` | Yes | When on, running on your mount costs no stamina while you are the rider and out of combat. Mount swimming still uses vanilla stamina. |
| `Free Stamina While Using Tools` | `true` | Yes | When on, axe and pickaxe swings cost no stamina while out of combat. Sneak attacks always cost stamina. |
| `Free Stamina While Building` | `true` | Yes | When on, placing and removing pieces costs no stamina while out of combat. |

## Server setup

The mod works without the server, but a dedicated server that also runs it can set and enforce the rules for everyone. Config sync uses [ServerSync](https://github.com/blaxxun-boop/ServerSync), which is built into `NonCombatStamina.dll`; there is nothing extra to install.

1. Install NonCombatStamina on the server the same way as on a client (`BepInEx/plugins`). On a dedicated server it only hosts the settings; it doesn't change any gameplay there.
2. Start the server once to create `BepInEx/config/TimeHorizon.NonCombatStamina.cfg`, then stop it and edit the values. For example, `Combat Grace Seconds = 10` for a longer grace window, or `Enable Unlimited Stamina = false` to switch the mod off for everyone.
3. Leave `Lock Configuration = true` (the default) to enforce the values. Start the server.

What players see:

- **Server with the mod, locked:** when a player with the mod joins, the server's values replace theirs for `Enable Unlimited Stamina`, `Combat Grace Seconds`, `Ground Tolerance` and the Advanced section toggles. Players can't change them in game; server admins (listed in `adminlist.txt`) still can, and their changes go to everyone. Each player's own config file is left untouched and applies again after they leave.
- **Server with the mod, unlocked:** players still receive the server's values on join, but anyone can change the synced settings in game, and the change is sent to the server and every connected player.
- **Server without the mod (vanilla):** nothing is synced. Each player's own config applies.
- **Player without the mod:** can still join a server that runs it and plays with vanilla stamina. The mod is not required.
- **Version check:** when both sides run the mod, the client and the server must both be at the same version. If not, the player is disconnected with a message naming the version needed.
- **Hosting from the game (no dedicated server):** the host's own settings are sent to the other players, and the host plays with the mod as usual.

## Compatibility

- **Client and server.** Install it on your own game. Installing it on the server is optional and only adds config sync and locking (see [Server setup](#server-setup)).
- **Works on vanilla servers.** Stamina is handled on your client (and on your mount while you ride it), so the server does not need the mod.
- **The server can't require it.** A server running the mod controls the settings for players who have it, but it doesn't kick players who don't; they simply play with vanilla stamina.
- **Other mods.** NonCombatStamina replaces the stamina call in `Player.CheckRun`, `Player.OnSneaking`, `Player.OnJump`, `Player.UpdateStats`, `Player.OnSwimming`, `Player.UpdatePlacement`, `SE_Stats.UpdateStatusEffect`, `Attack.Update` and `Sadle.UpdateRiding`, and the ground check in `Character.Jump`. It also watches `Character.ApplyDamage`, `Character.Damage` and `Humanoid.StartAttack`. Mods that rewrite those same calls may conflict. If NonCombatStamina can't find a call it expects, it leaves that method vanilla and logs a `Compatibility:` warning in `BepInEx/LogOutput.log`. Mods that watch `UseStamina` won't see the charges this mod skips.
- **AzuAreaRepair.** Azumatt's AzuAreaRepair works alongside this mod with no conflict. Repairs use their normal stamina cost.

## Known limitations

- **Animals count as combat.** A fleeing animal (deer, rabbits, boar and similar) marks you as targeted, so stamina costs return while you chase it and for the grace period afterwards. Hitting an animal also counts.
- **Fall damage doesn't count.** Falling and hurting yourself does not put you in combat.
- **Environmental damage counts.** Freezing, smoke, poison and burning ticks all start or extend combat, even with no enemy around.
- **Axe weapons count as tools.** Free swings are decided by the weapon's skill (Axes or Pickaxes), so battleaxes and other Axes-skill weapons swing free out of combat too.
- **Combat detection relies on the game's signals.** Creatures only put you in combat once they sense or target you, and that state is refreshed by creature AI over the network.
- **Vanilla stamina checks still apply.** You still need enough stamina to start an attack, and with an empty bar you can't run until it regenerates a little. The same applies to your mount.

## Building from source

This project targets .NET Framework 4.8 and needs a Valheim install with BepInEx plus publicized `assembly_valheim` and `assembly_utils` assemblies.

1. Copy `Environment.props.example` to `Environment.props` and set `ValheimDir` (and the other paths if yours differ). `Environment.props` is git-ignored.
2. Build the Release configuration with Visual Studio or MSBuild (`msbuild NonCombatStamina.csproj /restore /p:Configuration=Release`).
3. The output is `bin\Release\NonCombatStamina.dll`, with ServerSync already merged in.

If `Environment.props` is missing, the project falls back to `..\..\ValheimDev` relative to the project directory, with the usual `valheim_Data\Managed`, `publicized_assemblies`, `BepInEx\core` and `unstripped_corlib` layout.

## Support

Please file bug reports at https://github.com/TimeHorizonDev/NonCombatStamina/issues.

## License

NonCombatStamina is released under the MIT License. See [LICENSE](LICENSE).

This package includes [ServerSync](https://github.com/blaxxun-boop/ServerSync) by blaxxun-boop, licensed under MIT No Attribution (MIT-0). The license text is in `libs/ServerSync.LICENSE.txt`. ServerSync is merged into `NonCombatStamina.dll` at build time.
