# Skill Limit Extender Fork

A Valheim 1.0.x fork of SkillLimitExtender that raises skill caps beyond 100, adds configurable XP curves, and makes skill effects continue scaling safely past vanilla limits. It includes extended damage and blocking, asymptotic stamina/eitr/health and action-time scaling, safer tool costs, optional Jump-based safe-fall scaling, mod-skill support, UI fixes, save-data safeguards, and server-synchronized configuration.

This fork is intended as a **replacement** for the original SkillLimitExtender 1.2.0 plus its Valheim 1 compatibility/damage patches. Do not install those packages alongside this fork.

## Features

- Skill caps above the vanilla level-100 limit.
- Separate per-skill `Cap` and `BonusCap`.
- Custom XP growth curves for every skill.
- Damage scaling above level 100.
- Blocking scaling based on the same configurable extended multiplier as damage.
- Safe post-100 scaling for stamina, eitr, health costs, action times, and supported tool durability costs.
- Extended bow draw speed and crossbow reload speed.
- Extended Run, Swim, Dodge, Sneak stamina, Fishing, and Riding cost scaling.
- Optional Jump-based safe-fall-distance scaling.
- Automatic configuration sections for vanilla skills and discovered mod skills.
- Skill UI support for raw levels above 100.
- Character skill-data sanitization to prevent invalid skill entries from breaking the skills UI or save data.
- Server configuration synchronization and version/protocol checks.
- Normal BepInEx `.cfg` configuration only — **Jotunn and YamlDotNet are not required**.

## Requirements

- Valheim 1.0.x
- BepInExPack for Valheim

For multiplayer, use the same version of the mod on the server and clients.

## Installation

Install through a Thunderstore-compatible mod manager, or place the compiled DLL in:

```text
BepInEx/plugins/SkillLimitExtender/
```

Start the game once to generate:

```text
BepInEx/config/SkillLimitExtender.cfg
```

If upgrading from the original SkillLimitExtender setup, remove:

- the original `SkillLimitExtender` DLL/package;
- `SkillLimitExtenderValheim1Compat`;
- any separate SkillLimitExtender damage patch;
- old Jotunn/YamlDotNet dependencies if no other installed mod requires them.

Legacy `SLE_Skill_List.yaml` files are ignored by this fork.

# Default behavior

The default configuration is deliberately very different from vanilla. Skills can level to **1000**, while their normal skill-effect factor stops increasing at **500** unless `BonusCap` is changed.

## Default skill settings

Every vanilla skill starts with the following configuration:

| Setting | Vanilla behavior | Fork default |
|---|---|---|
| Maximum skill level | 100 | `Cap = 1000` |
| Maximum skill-effect factor | 1.0 at level 100 | `BonusCap = 500` → maximum factor 5.0 |
| Factor calculation | Effectively level / 100 up to 1.0 | `Relative = false` → level / 100, capped by `BonusCap` |
| Custom XP curve | No | `UseCustomGrowthCurve = true` |
| XP exponent | 1.5 | `GrowthExponent = 2.1` |
| XP multiplier | 0.5 | `GrowthMultiplier = 0.04` |
| XP constant | 0.5 | `GrowthConstant = 8.0` |

With the defaults:

```text
skillFactor = min(level / 100, BonusCap / 100)
```

Therefore:

| Skill level | Vanilla factor | Fork default factor |
|---:|---:|---:|
| 0 | 0.00 | 0.00 |
| 50 | 0.50 | 0.50 |
| 100 | 1.00 | 1.00 |
| 200 | capped at 1.00 | 2.00 |
| 300 | capped at 1.00 | 3.00 |
| 500 | capped at 1.00 | 5.00 |
| 1000 | capped at 1.00 | **5.00** because `BonusCap = 500` |

`Cap` controls how high the stored skill level can become. `BonusCap` separately controls how far most skill effects are allowed to scale.

## Default XP curve

The vanilla next-level requirement is:

```text
XP = nextLevel^1.5 * 0.5 + 0.5
```

The fork default is:

```text
XP = nextLevel^2.1 * 0.04 + 8.0
```

where:

```text
nextLevel = floor(currentLevel + 1)
```

The default curve is slightly easier through much of the early game, crosses vanilla around the higher vanilla levels, and becomes increasingly harder at extended levels.

| Current level | Vanilla XP for next level | Fork default XP for next level |
|---:|---:|---:|
| 0 | 1.00 | 8.04 |
| 10 | 18.74 | 14.15 |
| 50 | 182.61 | 162.15 |
| 100 | 508.02 | 655.34 |
| 200 | 1,425.33 | 2,754.45 |
| 300 | 2,611.58 | 6,420.83 |
| 500 | 5,607.45 | 18,702.73 |
| 999 | 15,811.89 | 79,818.49 |

## Default extended scaling

The global extended-scaling defaults are:

```ini
[3 - Extended Scaling]
EnableDamageScaling = true
DamageReferenceSkillLevel = 200
DamageReferenceMultiplier = 1.5
VanillaDamageMultiplier = 1.0
VanillaDamageExponent = 1.0
EnableExtendedCostScaling = true
ExtendedCostExponent = 0.42
EnableBlockingScaling = true
ScaleSafeFallDistance = true
SafeFallDistanceExponent = 2.0
```

With `DamageReferenceSkillLevel = 200` and `DamageReferenceMultiplier = 1.5`, the extended damage/block-bonus multiplier is calibrated so that level 200 gives exactly **1.5x the level-100 skill contribution**.

| Skill level | Effective factor | Damage multiplier vs. level 100 | Cost/time multiplier vs. level-100 value | Safe fall distance |
|---:|---:|---:|---:|---:|
| 100 | 1.00 | 1.000x | 1.000x | 4.00 m |
| 150 | 1.50 | 1.268x | 0.843x | 5.25 m |
| 200 | 2.00 | 1.500x | 0.747x | 7.00 m |
| 300 | 3.00 | 1.902x | 0.630x | 12.00 m |
| 500 | 5.00 | 2.564x | 0.509x | 28.00 m |
| 1000 | 5.00 | 2.564x | 0.509x | 28.00 m |

Level 1000 has the same default gameplay factor as level 500 because the default `BonusCap` is 500. Raise `BonusCap` if you want effects to continue increasing beyond level 500.

### What the shared cost curve means

For supported decreasing values, vanilla behavior is preserved through level 100. Above 100 the mod takes the exact vanilla level-100 value and reduces it asymptotically:

```text
value = valueAt100 / skillFactor^ExtendedCostExponent
```

This prevents stamina/eitr/health costs from reaching zero and becoming negative.

The table below shows the resulting total discount for several possible vanilla level-100 endpoints with the default `ExtendedCostExponent = 0.42`:

| Skill level | If vanilla gives 33% discount at 100 | If vanilla gives 50% discount at 100 | If vanilla gives 75% discount at 100 | If vanilla gives 80% discount at 100 |
|---:|---:|---:|---:|---:|
| 100 | 33.0% | 50.0% | 75.0% | 80.0% |
| 150 | 43.5% | 57.8% | 78.9% | 83.1% |
| 200 | 49.9% | 62.6% | 81.3% | 85.1% |
| 300 | 57.8% | 68.5% | 84.2% | 87.4% |
| 500 | 65.9% | 74.6% | 87.3% | 89.8% |

The value approaches zero but never reaches or crosses zero at any finite level.

### Damage

Levels 0-100 use the configurable vanilla-style damage curve:

```text
center = 0.4
       + 0.6
       * (level / 100)^VanillaDamageExponent
       * VanillaDamageMultiplier
```

The random damage range is based on that center with the normal `±0.15` range and vanilla clamping through level 100.

Above 100, the configured level-100 range becomes the baseline:

```text
finalMultiplier = skillFactor^e

e = ln(DamageReferenceMultiplier)
    / ln(DamageReferenceSkillLevel / 100)

extendedDamageRange = configuredLevel100Range * finalMultiplier
```

The defaults preserve vanilla damage from 0-100 and make level 200 deal **1.5x the configured level-100 damage range**.

### Blocking

Blocking keeps the level-0 block value unchanged and scales only the skill-derived bonus:

```text
finalBlockPower = blockAt0
                + (blockAt100 - blockAt0) * finalMultiplier
```

For example, if a shield has 78 block power at skill 0 and 117 at skill 100:

| Blocking level | Fork default block power |
|---:|---:|
| 0 | 78.0 |
| 100 | 117.0 |
| 200 | 136.5 |
| 300 | 152.2 |
| 500 | 178.0 |

The exact displayed value may be rounded by the game UI.

### Safe fall distance

When enabled, Jump skill above level 100 increases the no-damage fall breakpoint:

```text
safeFallDistance = 4
                 + (skillFactor^SafeFallDistanceExponent - 1)
```

The default exponent is `2.0`. The vanilla 4 m breakpoint is preserved exactly at factor 1.0.

This affects the safe fall breakpoint itself, so it also helps when falling without performing a jump.

# Configuration

The configuration file is:

```text
BepInEx/config/SkillLimitExtender.cfg
```

The global sections are intentionally prefixed so they stay at the top of the generated BepInEx configuration:

```text
[1 - Debug]
[2 - Server]
[3 - Extended Scaling]
```

Each skill then receives a normal section such as:

```text
[Swords]
[Bows]
[Jump]
[Run]
```

Discovered mod skills can also receive their own sections.

## `[1 - Debug]`

### `Enable Growth Curve Debug`

Default: `false`

Enables additional growth-curve logging. Leave this disabled during normal play unless diagnosing XP progression.

## `[2 - Server]`

### `LockConfiguration`

Default: `false`

When enabled on the server, the server sends its skill and extended-scaling gameplay configuration to connected clients. Clients then use the server-provided values for gameplay calculations.

## `[3 - Extended Scaling]`

### `EnableDamageScaling`

Default: `true`

Enables the fork's configurable damage curve and damage scaling above level 100.

### `DamageReferenceSkillLevel`

Default: `200`

Reference skill level at which `DamageReferenceMultiplier` is reached.

Must be above 100 for normal extended scaling.

```text
finalMultiplier = skillFactor^e

e = ln(DamageReferenceMultiplier)
    / ln(DamageReferenceSkillLevel / 100)
```

### `DamageReferenceMultiplier`

Default: `1.5`

Final extended multiplier reached at `DamageReferenceSkillLevel`.

The same curve is also used by optional Blocking scaling.

### `VanillaDamageMultiplier`

Default: `1.0`

Controls how much of the normal skill-derived damage increase is applied from levels 0-100.

- `1.0` = vanilla skill contribution.
- `0.5` = half of the vanilla skill contribution.
- It does **not** multiply the entire weapon's base damage.

```text
center = 0.4
       + 0.6
       * (level / 100)^VanillaDamageExponent
       * VanillaDamageMultiplier
```

### `VanillaDamageExponent`

Default: `1.0`

Controls the shape of damage progression from 0-100.

- `1.0` = vanilla progression shape.
- Above `1.0` = slower early progression.
- Below `1.0` = faster early progression.

```text
center = 0.4
       + 0.6
       * (level / 100)^VanillaDamageExponent
       * VanillaDamageMultiplier
```

### `EnableExtendedCostScaling`

Default: `true`

Extends supported decreasing skill-based values beyond level 100 using a safe asymptotic curve instead of allowing them to stop scaling or become zero/negative.

This includes supported stamina/eitr/health costs, bow/crossbow timing, movement/action costs, and supported build-tool costs.

### `ExtendedCostExponent`

Default: `0.42`

Controls how quickly supported costs/times continue decreasing above level 100.

Higher values reduce them faster.

```text
value = valueAt100 / skillFactor^ExtendedCostExponent
```

### `EnableBlockingScaling`

Default: `true`

Uses the damage-reference curve to extend only Blocking's skill-derived bonus above level 100.

```text
finalBlockPower = blockAt0
                + (blockAt100 - blockAt0) * finalMultiplier
```

### `ScaleSafeFallDistance`

Default: `true`

Allows Jump skill above level 100 to extend the safe fall-distance breakpoint.

### `SafeFallDistanceExponent`

Default: `2.0`

Controls the growth of the additional safe fall distance above Jump level 100.

```text
safeFallDistance = 4
                 + (skillFactor^SafeFallDistanceExponent - 1)
```

## Per-skill sections

Every skill section uses the same core parameters.

Example:

```ini
[Swords]
Cap = 1000
BonusCap = 500
Relative = false
UseCustomGrowthCurve = true
GrowthExponent = 2.1
GrowthMultiplier = 0.04
GrowthConstant = 8.0
```

### `Cap`

Default: `1000`

Maximum stored level the skill can reach.

```text
finalLevel <= Cap
```

### `BonusCap`

Default: `500`

Maximum skill-effect factor, expressed as a level-like value.

```text
100 = factor 1.0
250 = factor 2.5
500 = factor 5.0

maxSkillFactor = BonusCap / 100
```

`Cap` and `BonusCap` are independent. With the defaults, a skill can level from 500 to 1000 while its normal effect factor remains capped at 5.0.

### `Relative`

Default: `false`

Controls how the skill factor is calculated.

When `false`:

```text
skillFactor = level / 100
```

When `true`:

```text
skillFactor = (level / Cap) * (BonusCap / 100)
```

Both modes are capped by:

```text
BonusCap / 100
```

### `UseCustomGrowthCurve`

Default: `true`

Enables the custom XP requirement formula for this skill.

When disabled, vanilla XP progression is used.

### `GrowthExponent`

Default: `2.1`

Exponent used only for the XP required to gain the next level.

Vanilla equivalent: `1.5`.

### `GrowthMultiplier`

Default: `0.04`

Multiplier used only for the XP required to gain the next level.

Vanilla equivalent: `0.5`.

### `GrowthConstant`

Default: `8.0`

Constant added to the next-level XP requirement.

Vanilla equivalent: `0.5`.

The complete custom formula is:

```text
nextLevel = floor(currentLevel + 1)
XP = nextLevel^GrowthExponent * GrowthMultiplier + GrowthConstant
```

To reproduce vanilla XP requirements while still allowing an extended cap, use:

```ini
UseCustomGrowthCurve = true
GrowthExponent = 1.5
GrowthMultiplier = 0.5
GrowthConstant = 0.5
```

# Extended mechanics

The fork intentionally leaves mechanics that already scale sensibly alone and patches mechanics that either stop at level 100 or become invalid when extrapolated beyond 100.

Extended/safeguarded mechanics include:

- weapon and magic damage;
- Wood Cutting damage through the normal damage factor;
- Blocking skill bonus;
- melee/ranged/magic attack stamina costs;
- attack eitr costs;
- Blood Magic-style health costs;
- bow draw/hold stamina and eitr drain;
- bow draw speed;
- crossbow reload speed;
- Run stamina;
- Swim stamina;
- Dodge stamina;
- Sneak stamina;
- Fishing decreasing skill-based values;
- Riding stamina-related scaling;
- supported building/crafting tool stamina use;
- supported placement/tool durability consumption;
- optional safe-fall-distance scaling from Jump.

Sneak visibility is intentionally left unchanged.

Effects that already have meaningful natural caps, such as 100% bonus-item chance, are not forced beyond those caps.

# Commands

```text
sle_config_reload
```

Reloads the BepInEx configuration. On a server, changed locked configuration is broadcast to clients.

```text
sle_config_path
```

Prints the active Skill Limit Extender config path.

Vanilla `raiseskill` is patched to respect extended caps.

# Multiplayer and server configuration

When `LockConfiguration = true`, the server synchronizes the gameplay configuration to clients using the fork's internal config snapshot protocol.

The fork also performs a version/protocol handshake so incompatible SLE versions are not silently mixed in multiplayer.

For predictable multiplayer behavior, install the same release on the server and every client.

# Notes for users of the original SkillLimitExtender

This fork no longer uses YAML. All settings are in the normal BepInEx configuration file.

It also directly includes the Valheim 1.0 compatibility changes and extended damage logic, so separate compatibility/damage patches are not needed.

Do not run this fork together with the original SkillLimitExtender package.

# Credits

This project is based on **SkillLimitExtender** by **dyju420 / kydas420-damh901**:

- https://thunderstore.io/c/valheim/p/dyju420/SkillLimitExtender/
- https://github.com/kydas420-damh901/Skill-Limit-Extender

Valheim 1.0 compatibility work was informed by **Daishi11's SkillLimitExtenderValheim1Compat** package:

- https://thunderstore.io/c/valheim/p/Daishi11/SkillLimitExtenderValheim1Compat/

This fork is an independent continuation/fix and is not an official release of the original mod.

The upstream project is distributed under the MIT License. Preserve the upstream license/copyright notice when redistributing this fork.
