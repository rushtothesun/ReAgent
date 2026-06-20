# ReAgent Plugin Documentation

## 1. Introduction

ReAgent is an automation, state inspection, and overlay display plugin for Path of Exile 2 running through ExileCore2. It lets you create profiles, rule groups, and rules that react to the current game state.

The core rule model is still "if this, then that":

- A condition reads game state.
- If the condition is true, ReAgent performs an action.

The POE2 version has two rule systems:

- **ReAgent rules**: automation rules that press keys or run `SideEffect` actions.
- **ReAgentAura rules**: visual overlay rules that show icons and text displays.

## 2. Core Concepts

ReAgent configuration is organized hierarchically:

- **Profiles**: Top-level containers for different characters, builds, or activities. One profile is active at a time.
- **Rule Groups**: A profile contains groups. Groups can be enabled or disabled as a unit and restricted by area type.
- **Rules**: A group contains rules. ReAgent groups contain automation rules. ReAgentAura groups contain visual aura rules.

Profiles can be exported or imported from the profile controls. Groups can be exported from the group controls.

Groups are created from the group `+` button. The popup lets you choose:

| Group Type | Description |
| ---------- | ----------- |
| `ReAgent Rule` | Creates a normal automation group. |
| `ReAgentAura Rule` | Creates a visual overlay group. |

Group tabs are prefixed with `[R]` for ReAgent groups and `[A]` for ReAgentAura groups.

### 2.1. Group Controls

Every group has these controls:

| Control | Description |
| ------- | ----------- |
| `Enable` | Enables the group. Disabled groups do not evaluate. |
| `Enable in town` | Allows the group while `State.IsInTown` is true. |
| `Enable in hideout` | Allows the group while `State.IsInHideout` is true. |
| `Enable in maps` | Allows the group in non-town, non-hideout, non-peaceful areas. |
| `Enable in other peaceful areas` | Allows the group in peaceful areas that are not town or hideout. |
| `Name` | Group name. |
| `Export group` | Copies the group as an importable encoded string. |
| `Use Group Condition` | Enables a group-wide C# condition. |

ReAgentAura groups also have:

| Control | Description |
| ------- | ----------- |
| `Move Together` | When ReAgentAuras are unlocked, dragging one aura in the group moves all ReAgentAura rules in that group by the same mouse delta. |

### 2.2. Group Conditions

A group condition is optional. If enabled, it must return `true` before any rule in that group runs.

Group condition code uses the V2 C# syntax and receives `State`. It does not receive ReAgentAura `Display("name")`, because it gates the whole group rather than one aura rule.

Example: prevent a group from running while major panels are open.

```csharp
return !State.IsChatOpen
    && !State.IsLeftPanelOpen
    && !State.IsRightPanelOpen
    && !State.IsAnyFullscreenPanelOpen
    && !State.IsAnyLargePanelOpen;
```

For ReAgentAura groups, unlocked layout mode ignores the group condition so auras can still be placed. Normal locked rendering respects the group condition.

## 3. Rule Engine

ReAgent evaluates the active profile during `Render`.

Automation actions are paused when:

- The game window is not focused.
- The escape state is active and `EnableInEscapeState` is false.
- The player is dead.
- The player `Life`, `Buffs`, or `Actor` component cannot be read.
- The player has `grace_period` and `IgnoreGracePeriod` is false.

When automation is paused, ReAgent rules do not fire. ReAgentAuras can still render while unlocked so you can move and arrange them.

Key presses are throttled by `GlobalKeyPressCooldown`. A queued key press cannot be sent until the cooldown has elapsed and chat is not open.

### 3.1. ReAgent Rule Action Types

A ReAgent rule has an `Action type`.

| Action Type | Rule Source Result | What Happens |
| ----------- | ------------------ | ------------ |
| `Key` | `bool` | If true, ReAgent presses the configured keyboard key or controller key, depending on current input mode. |
| `SingleSideEffect` | `ISideEffect` | Returns one action object, such as `new RestartTimerSideEffect("name")`. Return `null` when no action should run. |
| `MultipleSideEffects` | `IEnumerable<ISideEffect>` | Returns multiple action objects. Return an empty collection when no action should run. |

ReAgentAura rules do not use these action types. ReAgentAura condition code returns `bool` and may enable display outputs with `Display("name")`.

### 3.2. V1 vs V2 Syntax

ReAgent rules support two code modes.

| Syntax | Engine | Notes |
| ------ | ------ | ----- |
| V1 | `System.Linq.Dynamic.Core` | One-expression syntax. The expression is parsed against `RuleState`, so properties can be referenced directly, such as `Vitals.HP.Percent < 50`. |
| V2 | Roslyn scripting | Multi-line C# method-body syntax. Use `State` to access the `RuleState` object and use `return` to return the result. Recommended for complex rules. |

V1 `Key` example:

```csharp
Vitals.HP.Percent < 50 && SinceLastActivation(1)
```

V2 `Key` example:

```csharp
return State.Vitals.HP.Percent < 50 &&
       State.SinceLastActivation(1);
```

V2 `SingleSideEffect` example:

```csharp
if (!State.Buffs.Has("tailwind"))
{
    return null;
}

return new DisplayTextSideEffect(
    "Tailwind active",
    new Vector2(600, 400),
    "Lime");
```

V2 `MultipleSideEffects` example:

```csharp
var tailwind = State.Buffs["tailwind"];

if (!tailwind.Exists)
{
    return Array.Empty<ISideEffect>();
}

return new ISideEffect[]
{
    new DisplayTextSideEffect("Tailwind", new Vector2(600, 400), "Lime"),
    new DisplayTextSideEffect($"{tailwind.TimeLeft:0.0}s", new Vector2(600, 420), "White")
};
```

### 3.3. V2 Script Imports

V2 ReAgent rules, group conditions, and ReAgentAura conditions share the same base imports.

Commonly useful imports include:

- `System`
- `System.Collections.Generic`
- `System.Linq`
- `System.Numerics`
- `System.Windows.Forms`
- `ReAgent`
- `ReAgent.State`
- `ReAgent.SideEffects`
- `ReAgent.ReAgentAuras`
- `ExileCore2`
- `ExileCore2.Shared`
- `ExileCore2.Shared.Enums`
- `ExileCore2.Shared.Helpers`
- `ExileCore2.PoEMemory`
- `ExileCore2.PoEMemory.Components`
- `ExileCore2.PoEMemory.MemoryObjects`
- `ExileCore2.PoEMemory.FilesInMemory`
- `GameOffsets2`
- `GameOffsets2.Native`

Because `System.Numerics` is imported, use `Vector2` and `Vector3` directly. Because `System.Windows.Forms` is imported, use `Keys.D1`, `Keys.Space`, and similar keyboard constants directly.

### 3.4. Keyboard and Controller Key Rules

For `Key` action rules, ReAgent stores separate keyboard and controller bindings:

- `Keyboard` binding uses `KeyV2`.
- `Controller` binding uses `ControllerKeyV2`.

When the game is using controller input mode, ReAgent presses the controller binding. Otherwise, it presses the keyboard binding.

If the active input mode has no assigned key, the rule throws an error such as `Controller key is not assigned` or `Keyboard key is not assigned`.

Use `State.IsUsingController` inside conditions when the rule logic itself should differ by input mode. Use `State.Controller` to check live XInput button state or skill-bar texture bindings.

## 4. `RuleState`: Game State Available to Rules

In V1 ReAgent rule expressions, the current `RuleState` members can be referenced directly. In V2 code, use the `State` parameter.

Example:

```csharp
return State.Buffs["tailwind"].Exists;
```

### 4.1. Player Information

| Member | Type | Description |
| ------ | ---- | ----------- |
| `Player` | `MonsterInfo` | The player entity represented with the same type used for monsters. |
| `Vitals` | `VitalsInfo` | Player health, energy shield, mana, and ward. |
| `Buffs` | `BuffDictionary` | Player buffs and debuffs. |
| `Ailments` | `IReadOnlyCollection<string>` | Custom ailment group names from `CustomAilments.json` that match active player buffs. |
| `Skills` | `SkillDictionary` | Player skills on the active weapon set. |
| `WeaponSwapSkills` | `SkillDictionary` | Player skills on the inactive weapon set. |
| `ActiveWeaponSetIndex` | `int` | Current weapon set index from stats. In current ExileCore data, set 1 is usually `0` and set 2 is usually `1`. |
| `Animation` | `AnimationE` | Current player animation enum. |
| `AnimationId` | `int` | Numeric current animation id. |
| `AnimationStage` | `int` | Numeric current animation stage. |
| `IsMoving` | `bool` | True when the player actor reports movement. |
| `Flasks` | `FlasksInfo` | POE2 flask slots exposed by this plugin. |
| `MapStats` | `StatDictionary` | Current map stats. |
| `MousePosition` | `Vector2` | World mouse position converted to grid coordinates. |

### 4.2. Area and UI State

| Member | Type | Description |
| ------ | ---- | ----------- |
| `IsInHideout` | `bool` | True in hideout. |
| `IsInTown` | `bool` | True in town. |
| `IsInPeacefulArea` | `bool` | True in any peaceful area. |
| `IsInEscapeMenu` | `bool` | True while the escape state is active. |
| `AreaName` | `string` | Current area name. |
| `IsChatOpen` | `bool` | True when the chat title panel is visible. |
| `IsLeftPanelOpen` | `bool` | True when ExileCore reports the left panel visible. |
| `IsRightPanelOpen` | `bool` | True when ExileCore reports the right panel visible. |
| `IsAnyFullscreenPanelOpen` | `bool` | True when any fullscreen panel is visible. |
| `IsAnyLargePanelOpen` | `bool` | True when any large panel is visible. |

### 4.3. Controller State

| Member | Type | Description |
| ------ | ---- | ----------- |
| `IsUsingController` | `bool` | True when ExileCore reports the game is currently using controller input mode. |
| `Controller` | `ControllerState` | XInput controller state and controller skill binding helpers. |

Example:

```csharp
return State.IsUsingController &&
       State.Controller.IsPressed(ExileCore2.Shared.Nodes.HotkeyNodeV2.ControllerKey.LTrigger);
```

Use the `ShowControllerState` setting to open a live controller window with button state, trigger pressure, primary/secondary skill-bar bindings, and copyable skill icon texture names.

In V2 code, use the fully qualified controller key enum name unless you add your own alias:

```csharp
ExileCore2.Shared.Nodes.HotkeyNodeV2.ControllerKey
```

`ControllerState` contains:

| Property or Method | Type | Description |
| ------------------ | ---- | ----------- |
| `IsConnected` | `bool` | True when XInput state was read successfully. |
| `LeftTriggerPressure` | `byte` | Raw left trigger pressure. |
| `RightTriggerPressure` | `byte` | Raw right trigger pressure. |
| `Skills` | `ControllerSkillBindings` | Maps controller skill-bar icon texture names to the controller buttons currently bound to those skills. Use this after copying a texture name from `ShowControllerState`. |
| `IsPressed(HotkeyNodeV2.ControllerKey key)` | `bool` | True if the requested controller key is pressed. Triggers use pressure greater than `30`. |

`ControllerSkillBindings` reads the visible controller HUD skill bars. It is useful when a rule should react to where a skill is currently bound instead of hard-coding a controller button.

Typical workflow:

1. Enable `ShowControllerState`.
2. Find the skill in the Primary Skill Bar or Secondary Skill Bar table.
3. Copy the texture file name, such as `RangerSnipeShotArrow.dds`.
4. Use `PrimaryKeyForTexture(...)`, `SecondaryKeyForTexture(...)`, `HasPrimaryTexture(...)`, or `HasSecondaryTexture(...)` in a rule.

`ControllerSkillBindings` contains:

| Method | Return Type | Description |
| ------ | ----------- | ----------- |
| `PrimaryKeyForTexture(string textureName)` | `HotkeyNodeV2.ControllerKey` | Finds the primary skill-bar controller key for a skill icon texture. |
| `SecondaryKeyForTexture(string textureName)` | `HotkeyNodeV2.ControllerKey` | Finds the secondary skill-bar controller key for a skill icon texture. |
| `HasPrimaryTexture(string textureName)` | `bool` | True if the primary bar has a matching texture. |
| `HasSecondaryTexture(string textureName)` | `bool` | True if the secondary bar has a matching texture. |

Texture matching accepts an exact texture string, matching file name, or texture suffix.

Example: release the controller button currently bound to Snipe after the Snipe channel reaches the perfect timing animation stage.

```csharp
var snipeKey = State.Controller.Skills.PrimaryKeyForTexture("RangerSnipeShotArrow.dds");

return State.SinceLastActivation(0.5)
    && State.Animation.ToString() == "SnipeChannel"
    && State.AnimationStage > 17
    && snipeKey != ExileCore2.Shared.Nodes.HotkeyNodeV2.ControllerKey.None
    ? new ReleaseKeyHoldSideEffect(
        new ExileCore2.Shared.Nodes.HotkeyNodeV2.HotkeyNodeValue(snipeKey))
    : null;
```

### 4.4. Entities and Monsters

| Member | Return Type | Description |
| ------ | ----------- | ----------- |
| `Monsters()` | `IEnumerable<MonsterInfo>` | Hostile valid monsters up to `MaximumMonsterRange`. |
| `Monsters(int range)` | `IEnumerable<MonsterInfo>` | Hostile valid monsters within `range`, any rarity. |
| `Monsters(int range, MonsterRarity rarity)` | `IEnumerable<MonsterInfo>` | Hostile valid monsters within `range` matching `rarity`. |
| `MonsterCount()` | `int` | Count of hostile valid monsters up to `MaximumMonsterRange`. |
| `MonsterCount(int range)` | `int` | Count of hostile valid monsters within `range`. |
| `MonsterCount(int range, MonsterRarity rarity)` | `int` | Count of hostile valid monsters within `range` matching `rarity`. |
| `FriendlyMonsters` | `IEnumerable<MonsterInfo>` | Friendly valid monsters found while building nearby monster state. |
| `AllMonsters` | `IEnumerable<MonsterInfo>` | Valid visible monsters, including dead or non-hostile entries that pass the broader valid-monster filter. |
| `HiddenMonsters` | `IEnumerable<MonsterInfo>` | Monsters with the `hidden_monster` buff. |
| `Corpses` | `IEnumerable<MonsterInfo>` | Dead monster entities that pass the broader valid-monster filter. |
| `AllPlayers` | `IEnumerable<MonsterInfo>` | Player entities in the area. |
| `PlayerByName(string name)` | `MonsterInfo` | First player whose `PlayerName` equals `name`, or null if not found. |
| `MiscellaneousObjects` | `IEnumerable<EntityInfo>` | Entities with ExileCore type `MiscellaneousObjects`. |
| `NoneEntities` | `IEnumerable<EntityInfo>` | Entities with ExileCore type `None`. |
| `IngameIcons` | `IEnumerable<EntityInfo>` | Entities with ExileCore type `IngameIcon`. |
| `MiniMonoliths` | `IEnumerable<EntityInfo>` | Entities with ExileCore type `MiniMonolith`. |
| `Chests` | `IEnumerable<EntityInfo>` | Chest entities. |
| `TerrainEntities` | `IEnumerable<EntityInfo>` | Terrain entities. |
| `Effects` | `IEnumerable<EntityInfo>` | Effect entities. |
| `PortalExists(int distance)` | `bool` | True if a town portal entity is within `distance`. |

Nearby monster lists are limited by the `MaximumMonsterRange` setting before the per-call range filter is applied.

`EntityInfo` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Id` | `uint` | Entity id. |
| `Path` | `string` | Entity path. |
| `Metadata` | `string` | Entity metadata path. |
| `BaseEntityPath` | `string` | Base animated object path when available. |
| `AttachedAnimatedObjects` | `List<EntityInfo>` | Attached animated object entities when available. |
| `Position` | `Vector3` | World position. |
| `GridPosition` | `Vector2` | Grid position. |
| `Position2D` | `Vector2` | X/Y projection of `Position`. |
| `Distance` | `float` | Distance from player. |
| `DistanceToCursor` | `float` | Distance to world mouse cursor grid position. |
| `VectorToCursor` | `Vector2` | Vector from entity to cursor. |
| `VectorToPlayer` | `Vector2` | Vector from entity to player. |
| `AngleToCursor` | `float` | Angle helper relative to cursor and player. |
| `DistanceToCursorAngle` | `float` | Perpendicular distance helper relative to cursor angle. |
| `Scale` | `float` | Positioned component scale, or `0`. |
| `Stats` | `StatDictionary` | Entity stats. |
| `States` | `StateDictionary` | Entity state-machine states by string key. |
| `IsAlive` | `bool` | True if alive. |
| `IsTargeted` | `bool` | True if targeted. |
| `IsTargetable` | `bool` | True if targetable. |
| `IsUsingAbility` | `bool` | True if actor action is `UsingAbility`. |
| `PlayerName` | `string` | Player name when this entity is a player entity. |

`MonsterInfo` inherits `EntityInfo` and adds:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Vitals` | `VitalsInfo` | Monster/player life information. |
| `Actor` | `ActorInfo` | Actor action and animation data. |
| `IsInvincible` | `bool` | True when `GameStat.CannotBeDamaged` has a non-zero value. |
| `Rarity` | `MonsterRarity` | Normal, Magic, Rare, or Unique. |
| `Buffs` | `BuffDictionary` | Entity buffs. |
| `Skills` | `SkillDictionary` | Entity skills. |

`ReAgent.State.MonsterRarity` is a flags enum. In V2 code, use the fully qualified `ReAgent.State.MonsterRarity` name because `MonsterRarity` is ambiguous with `ExileCore2.Shared.Enums.MonsterRarity`.

| Value | Description |
| ----- | ----------- |
| `Normal` | Normal monsters. |
| `Magic` | Magic monsters. |
| `Rare` | Rare monsters. |
| `Unique` | Unique monsters. |
| `Any` | Normal, Magic, Rare, and Unique. |
| `AtLeastRare` | Rare or Unique. |

Example:

```csharp
return State.Monsters(
    50,
    ReAgent.State.MonsterRarity.Rare | ReAgent.State.MonsterRarity.Unique).Any();
```

### 4.5. Internal Rule State Helpers

These helpers are scoped to the current rule group.

| Member | Return Type | Description |
| ------ | ----------- | ----------- |
| `IsKeyPressed(Keys key)` | `bool` | True if the keyboard key is currently down. |
| `SinceLastActivation(double seconds)` | `bool` | True if the current rule has not successfully applied a side effect for more than `seconds`. |
| `IsFlagSet(string name)` | `bool` | Reads a group-scoped boolean flag. |
| `GetNumberValue(string name)` | `float` | Reads a group-scoped number. Missing numbers return `0`. |
| `GetTimerValue(string name)` | `float` | Reads elapsed seconds from a group-scoped timer. Missing timers return `0`. |
| `IsTimerRunning(string name)` | `bool` | True if the named group-scoped timer exists and is running. |
| `Random(int min, int max)` | `float` | Returns a random integer value from `min` inclusive to `max` exclusive, typed as `float`. |

`SinceLastActivation` is updated when a side effect is successfully applied, not merely when a condition evaluates to true.

## 5. Data Types

### 5.1. `VitalsInfo` and `Vital`

`VitalsInfo` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `HP` | `Vital` | Health. |
| `ES` | `Vital` | Energy shield. |
| `Mana` | `Vital` | Mana. |
| `Ward` | `Vital` | Ward. |

`Vital` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Current` | `double` | Current value. |
| `Max` | `double` | Maximum unreserved value. |
| `Percent` | `double` | `Current / Max * 100`. |

Example:

```csharp
return State.Vitals.HP.Percent < 40 ||
       State.Vitals.ES.Percent < 20;
```

### 5.2. `BuffDictionary` and `StatusEffect`

`BuffDictionary` contains:

| Member | Type | Description |
| ------ | ---- | ----------- |
| `this[string id]` | `StatusEffect` | Returns the first buff with this internal name, or an empty `StatusEffect` with `Exists == false`. |
| `Has(string id)` | `bool` | True if a buff with this internal name exists. |
| `AllBuffs` | `List<StatusEffect>` | All buff rows, including duplicates. |

Duplicate buff rows matter. POE2 effects can expose stacks as repeated buff rows rather than a single row with a stack count. For those, use `AllBuffs.Count(...)` or ReAgentAuras `Show Instance Count`.

`StatusEffect` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Name` | `string` | Internal buff name. |
| `DisplayName` | `string` | Display name from the buff. |
| `Exists` | `bool` | True if the status exists. |
| `TimeLeft` | `double` | Remaining seconds. Permanent effects may use very large or non-finite timer values. |
| `TotalTime` | `double` | Total duration. |
| `PercentTimeLeft` | `double` | Remaining duration percentage. Returns `100` for positive infinity and `0` for missing buffs. |
| `Charges` | `int` | Buff charges from ExileCore's `BuffCharges`. |
| `Stacks` | `int` | Buff stacks from ExileCore's `BuffStacks`. |
| `FlaskSlot` | `int` | Flask slot associated with the buff, if any. |
| `Skill` | `SkillInfo` | Player skill that sourced this buff when it can be resolved. |

Examples:

```csharp
return State.Buffs["herald_of_ice"].Exists;
```

Tailwind exposes one buff row per stack and maxes at 10 stacks:

```csharp
var tailwindCount = State.Buffs.AllBuffs.Count(x => x.Name == "tailwind");
return tailwindCount == 10;
```

### 5.3. `SkillDictionary` and `SkillInfo`

`SkillDictionary` contains:

| Member | Type | Description |
| ------ | ---- | ----------- |
| `this[string id]` | `SkillInfo` | Skill by internal skill name, or an empty skill with `Exists == false`. |
| `Current` | `SkillInfo` | Current actor action skill, or empty if none. |
| `BySlotIndex(int slotIndex)` | `SkillInfo` | Skill assigned to a specific skill slot index, or empty if none. |
| `Has(string id)` | `bool` | True if the skill dictionary contains the named skill. |
| `AllSkills` | `List<SkillInfo>` | All named actor skills, ordered by name. |

`SkillInfo` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Name` | `string` | Internal skill name. |
| `Exists` | `bool` | True if the skill exists. |
| `CanBeUsed` | `bool` | True if the skill can be used with the current weapon and current life/mana/ES pools. |
| `IsUsing` | `bool` | True if the skill is currently being used. |
| `UseStage` | `int` | Current skill use stage. |
| `ManaCost` | `int` | Mana cost. |
| `LifeCost` | `int` | Life cost. |
| `EsCost` | `int` | Energy shield cost. |
| `MaxUses` | `int` | Maximum stored uses for skills with cooldown charges. |
| `MaxCooldown` | `float` | Skill cooldown value from ExileCore. |
| `RemainingUses` | `int` | Remaining cooldown uses. |
| `Cooldowns` | `List<float>` | Remaining cooldowns for cooldown instances. |
| `CastTime` | `float` | Cast time in seconds. |
| `DeployedEntities` | `List<MonsterInfo>` | Entities deployed by the skill, such as minions or other spawned objects when exposed by ExileCore. |
| `Stats` | `StatDictionary` | Skill stats. |

The record also has public numeric `Id` and `Id2` values, but normal rules should prefer `Name`, `BySlotIndex`, or `AllSkills`.

### 5.4. `ActorInfo`

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Action` | `string` | Actor action enum as text, or null. |
| `Animation` | `string` | Actor animation enum as text, or null. |
| `CurrentAnimationId` | `int` | Numeric current animation id. |
| `CurrentAnimationStage` | `int` | Numeric current animation stage. |
| `AnimationProgress` | `float` | Current animation progress from `0.0` to `1.0`. |

### 5.5. `FlasksInfo` and `FlaskInfo`

This POE2 ReAgent implementation exposes two flask slots.

`FlasksInfo` contains:

| Member | Type | Description |
| ------ | ---- | ----------- |
| `this[int i]` | `FlaskInfo` | Zero-based flask slot. Valid indexes are `0` and `1`. |
| `Flask1` | `FlaskInfo` | Same as `Flasks[0]`. |
| `Flask2` | `FlaskInfo` | Same as `Flasks[1]`. |

`FlaskInfo` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Active` | `bool` | True if one of the flask's known buff names is active. |
| `CanBeUsed` | `bool` | True if the item has at least `ChargesPerUse` charges. |
| `Charges` | `int` | Current charges. |
| `MaxCharges` | `int` | Maximum charges. |
| `ChargesPerUse` | `int` | Charges consumed per use. |
| `ClassName` | `string` | Base item class name. |
| `BaseName` | `string` | Base item name. |
| `UniqueName` | `string` | Unique item name if present. |
| `Name` | `string` | `UniqueName` when present, otherwise `BaseName`. |
| `CanBeUsedIn` | `float` | Reserved cooldown-style value. Current implementation returns `0` for present flask items and `100` for empty slots. |

Example:

```csharp
return State.Flasks.Flask1.CanBeUsed &&
       State.Vitals.HP.Percent < 50;
```

### 5.6. `StatDictionary`, `StateDictionary`, and `Stat`

`StatDictionary` uses `GameStat` keys:

```csharp
return State.Player.Stats[GameStat.CannotBeDamaged].Exists;
```

`StateDictionary` uses string keys:

```csharp
return State.Player.States.Has("some_state_name");
```

Both dictionaries expose:

| Member | Description |
| ------ | ----------- |
| `this[key]` | Returns a `Stat`. |
| `Has(key)` | True if the key exists. |

`Stat` contains:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Exists` | `bool` | True if the stat or state key exists. |
| `Value` | `int` | Stat or state value. Missing entries return `0`. |

## 6. Available `SideEffect` Actions

Side effects are used by ReAgent rules with action type `SingleSideEffect` or `MultipleSideEffects`.

### 6.1. Key Presses and Holds

| SideEffect | Description |
| ---------- | ----------- |
| `PressKeySideEffect(HotkeyNodeValue Key)` | Queues one key press. |
| `PressKeySideEffect(Keys key)` | Convenience constructor for keyboard keys. |
| `StartKeyHoldSideEffect(HotkeyNodeValue Key)` | Starts holding a key. |
| `StartKeyHoldSideEffect(Keys key)` | Convenience constructor for keyboard keys. |
| `ReleaseKeyHoldSideEffect(HotkeyNodeValue Key)` | Releases a held key. |
| `ReleaseKeyHoldSideEffect(Keys key)` | Convenience constructor for keyboard keys. |

For `PressKeySideEffect`, only one key press can be queued at a time and it respects `GlobalKeyPressCooldown`. Holds are sent every frame while requested by rules.

Example:

```csharp
return new PressKeySideEffect(Keys.D1);
```

### 6.2. Timers, Flags, and Numbers

These values are scoped to the current rule group.

| SideEffect | Description |
| ---------- | ----------- |
| `StartTimerSideEffect(string Id)` | Starts a named timer if it is not already running. |
| `StopTimerSideEffect(string Id)` | Stops a running timer. |
| `RestartTimerSideEffect(string Id)` | Creates or resets a named timer and starts it. |
| `ResetTimerSideEffect(string Id)` | Removes a named timer. |
| `SetFlagSideEffect(string Id)` | Sets a named flag to true. |
| `ResetFlagSideEffect(string Id)` | Removes a named flag. |
| `SetNumberSideEffect(string Id, float Value)` | Sets a named number. |
| `ResetNumberSideEffect(string Id)` | Removes a named number. |

Example:

```csharp
return new ISideEffect[]
{
    new SetFlagSideEffect("boss-seen"),
    new RestartTimerSideEffect("boss-timer")
};
```

### 6.3. Display Side Effects

Display side effects are immediate ReAgent drawing actions. They are separate from ReAgentAuras.

| SideEffect | Description |
| ---------- | ----------- |
| `DisplayTextSideEffect(string Text, Vector2 Position, string Color)` | Draws text at a screen position with a black background. |
| `DisplayGraphicSideEffect(string GraphicFilePath, Vector2 Position, Vector2 Size, string ColorTint)` | Draws an image from `ImageDirectory`. |
| `ProgressBarSideEffect(string Text, Vector2 Position, Vector2 Size, float Fraction, string Color, string BackgroundColor, string TextColor)` | Draws a progress bar. |

`DisplayGraphicSideEffect` resolves images relative to the ExileCore directory plus the `ImageDirectory` setting. The default image directory is `textures/ReAgent`.

Example:

```csharp
return new ProgressBarSideEffect(
    "HP",
    new Vector2(500, 500),
    new Vector2(120, 16),
    (float)(State.Vitals.HP.Percent / 100.0),
    "Red",
    "Black",
    "White");
```

### 6.4. Advanced Side Effects

| SideEffect | Description |
| ---------- | ----------- |
| `DelayedSideEffect(double Delay, Func<IReadOnlyList<ISideEffect>> SideEffects)` | Waits `Delay` seconds, then applies the returned side effects. |
| `DelayedSideEffect(double Delay, IReadOnlyList<ISideEffect> SideEffects)` | Convenience constructor for a fixed side-effect list. |
| `DisconnectSideEffect()` | Attempts to close TCP connections for the game process. |
| `PluginBridgeSideEffect<T>(string MethodName, Action<T> InvokeFunctionAction)` | Calls an ExileCore PluginBridge method. |

## 7. ReAgentAuras

ReAgentAuras is a visual aura system built into ReAgent. It is inspired by WeakAuras-style overlays: create icons, choose visuals, add optional text displays, and control them with condition code.

Create ReAgentAuras by making a `ReAgentAura Rule` group from the group `+` button. In a ReAgentAura group, `Add New Rule` adds another ReAgentAura rule.

### 7.1. Rule Fields

| Field | Description |
| ----- | ----------- |
| `Rule Name` | Display/editor name for the aura. |
| `Source Name` | Internal buff or skill name used for default display values and icon extraction. |
| `Frame` | Optional overlay frame. Options are `None`, `buff`, `charges`, `debuff`, `minionframe`, and `nopausebuffframe`. |
| `Position X` / `Position Y` | Screen position of the icon. Can also be changed by dragging while ReAgentAuras are unlocked. |
| `Icon Size` | Screen size of the icon. |
| `Visual` | One of `Color`, `Icon`, or `Manual Icon`. |

### 7.2. Visual Modes

| Visual | Behavior |
| ------ | -------- |
| `Color` | Draws a colored square using the chosen color. |
| `Icon` | Uses a DDS icon discovered from the active source buff or player skill, extracts it from `Content.ggpk`, converts it to PNG, and registers it with ExileCore. |
| `Manual Icon` | Uses an existing PNG path on disk. |

For `Manual Icon`, enter the PNG path and press `Register Icon`.
ReAgent validates that the file exists, creates a texture key from the manual icon path, and registers the image with ExileCore.
Changing the manual icon path changes the texture key so a previous registered image is not reused accidentally.

For `Icon`, the source must be discoverable:

- If `Source Name` matches an active player buff, ReAgent first tries the buff visual DDS path.
- If the buff visual has no DDS, ReAgent tries the source skill icon DDS.
- If `Source Name` matches a player actor skill, ReAgent tries the skill's active skill icon DDS.

Buffs without an exposed icon DDS path need `Manual Icon` or a different source.

### 7.3. Asset Extraction

ReAgentAura extraction settings live under the `ReAgentAuras` settings header.

| Control | Description |
| ------- | ----------- |
| `Enable Extraction` | Enables frame and icon extraction controls. |
| `Extract Frames` | Extracts built-in frame assets. Existing frame PNGs are skipped. |
| `Auto-Detect GGPK` | Sets `Content.ggpk Path` from the game process directory when possible. |
| `Content.ggpk Path` | Path to standalone POE2 `Content.ggpk`. |

If the GGPK path is blank, ReAgent tries to auto-detect it once per ExileCore run. The manual button can retry.

Extracted assets are written under the plugin config directory:

| Asset Type | Output Folder |
| ---------- | ------------- |
| Frames | `ReAgentAuras/Frames` |
| Icons | `ReAgentAuras/Icons` |

DDS files are temporary. ReAgent converts them to PNG and removes the DDS when icon extraction succeeds.

Icon output names are flattened and include a stable hash of the source GGPK path, such as:

```text
gatherwinds-27019953.png
```

This prevents long nested output folders and avoids collisions between different DDS paths with the same file name.

### 7.4. Frames

Available frame names:

| Frame | Source DDS | Output PNG | Notes |
| ----- | ---------- | ---------- | ----- |
| `buff` | `art/textures/interface/2d/2dart/uiimages/ingame/4k/buff.dds` | `buff.png` | Normal buff-style frame. |
| `charges` | `art/textures/interface/2d/2dart/uiimages/ingame/4k/charges.dds` | `charges.png` | Charge-style frame. |
| `debuff` | `art/textures/interface/2d/2dart/uiimages/ingame/4k/debuff.dds` | `debuff.png` | Debuff-style frame. |
| `minionframe` | `art/textures/interface/2d/2dart/uiimages/ingame/4k/minionframe.dds` | `minionframe.png` | Minion-style frame. |
| `nopausebuffframe` | `art/textures/interface/2d/2dart/uiimages/ingame/4k/nopausebuffframe.dds` | `nopausebuffframe.png` | No-pause buff frame. |

Frames are drawn over icons. The current layout uses normalized frame dimensions around 132x132 with an inner icon scale of `0.625` and a small upward Y offset.

If a selected frame is not extracted yet, the editor shows `Frame not extracted.` and the runtime does not draw that frame.

### 7.5. Displays

Displays are optional text outputs attached to a ReAgentAura rule.

Each display has:

| Field | Description |
| ----- | ----------- |
| `Name` | Required. Must be unique within the same ReAgentAura rule. Other rules may reuse the same display name. |
| `Effect` | Default value provider. |
| `Start Position` | Initial anchor relative to the icon: `Bottom`, `Top`, `Left`, `Right`, or `Center`. |
| `Offset X` / `Offset Y` | Screen-space offset from the start position. |
| `Text Scale` | Text scale used by ExileCore drawing. |
| `Text Color` | Text color. |

Display effects:

| Effect | Default `Value` |
| ------ | --------------- |
| `Show Timer` | Minimum finite `TimeLeft` from buffs whose `Name` matches `Source Name`, formatted like `3.2s`. |
| `Show Charges` | Maximum `Charges` from matching buff rows. |
| `Show Instance Count` | Count of matching buff rows. Useful for effects represented by repeated buff instances. |
| `Show Stack` | Maximum `Stacks` from matching buff rows. |
| `Show Custom Text` | No default value. Displays `Text` set in condition code. |

Built-in display effects render their default `Value`. Setting `Text` on a built-in display effect throws an error. Use `Show Custom Text` for text based on a skill, cooldown, custom calculation, or non-buff state.

### 7.6. Condition Code

ReAgentAura condition code is V2-style C# with two parameters:

| Parameter | Description |
| --------- | ----------- |
| `State` | Current `RuleState`. |
| `Display(string name)` | Gets a named display for this aura rule. |

The condition must return `true` for the aura icon to be active. In locked mode, inactive auras are not drawn. In unlocked mode, inactive auras are still shown for placement and are labeled idle.

Displays only draw when:

- The aura condition returns `true`.
- The display exists.
- The condition code sets `Display("Name").Enabled = true`.
- The display resolves non-empty text.

Display text resolution depends on the display effect:

- `Show Timer`, `Show Charges`, `Show Instance Count`, and `Show Stack` render `Value`.
- `Show Custom Text` renders `Text`.

`Display("Name")` only searches displays on the same ReAgentAura rule. It does not affect displays in other rules, even if they have the same name.

`Display("Name")` returns a `ReAgentAuraDisplayRuntime`.

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Name` | `string` | Display name. |
| `Enabled` | `bool` | Set this to true to draw the display. Defaults to false every evaluation. |
| `Value` | `string` | Default text calculated from the display effect. |
| `Text` | `string` | Custom text used only by `Show Custom Text` displays. Setting this on a built-in display effect throws an error. |

Example: show a default Tailwind timer.

```csharp
var buff = State.Buffs["tailwind"];
var timer = Display("Timer");

timer.Enabled = buff.Exists;

return buff.Exists;
```

Example: prefix the default timer text with a separate custom display.

Display setup:

- `Timer`: `Show Timer`
- `Label`: `Show Custom Text`

```csharp
var buff = State.Buffs["tailwind"];
var timer = Display("Timer");
var label = Display("Label");

timer.Enabled = buff.Exists;
label.Enabled = buff.Exists && !string.IsNullOrWhiteSpace(timer.Value);
label.Text = $"Tailwind: {timer.Value}";

return buff.Exists;
```

Example: show custom mark status text.

Display setup:

- `MarkStatus`: `Show Custom Text`

```csharp
var skill = State.Skills["FreezingMarkPlayer"];
if (!skill.Exists)
{
    return false;
}

var buff = State.Buffs["freezing_mark_damage_buff"];
var status = Display("MarkStatus");

if (buff.Exists && buff.TimeLeft > 4)
{
    return false;
}

status.Enabled = true;
status.Text = buff.Exists ? $"{buff.TimeLeft:0.0}s" : "Ready";

return true;
```

Example: show a custom count of repeated Tailwind instances.

Display setup:

- `TailwindStacks`: `Show Custom Text`

```csharp
var count = State.Buffs.AllBuffs.Count(x => x.Name == "tailwind");
var stacks = Display("TailwindStacks");

stacks.Enabled = count > 0;
stacks.Text = count.ToString();

return count > 0;
```

### 7.7. Limitations and Notes

- ReAgentAura displays are text outputs. The icon itself is controlled by the condition return value and visual settings.
- Default display values are sourced from player buffs matching `Source Name`.
- Skills can expose useful cooldown and use data through `State.Skills`, but not every game UI counter is exposed as structured API data.
- Icon extraction requires the standalone POE2 `Content.ggpk`. Steam installs may not have a `Content.ggpk` file.
- ExileCore needs images on disk to register and draw them. ReAgent extracts and converts icons to PNG files before registering them.

## 8. Custom Ailments

`CustomAilments.json` maps friendly ailment group names to lists of internal buff names. During state creation, ReAgent checks the player buffs and populates `State.Ailments` with any matching group names.

Example:

```json
{
  "MyCurseGroup": [
    "curse_temporal_chains",
    "curse_enfeeble"
  ]
}
```

Rule example:

```csharp
return State.Ailments.Contains("MyCurseGroup");
```

Default groups currently included in `CustomAilments.json`:

| Group Name | Purpose |
| ---------- | ------- |
| `Bleeding` | Bleeding and puncture-style effects. |
| `Bleeding Or Corruption` | Bleeding plus corrupted blood style effects. |
| `Burning` | Burning and ignite-style effects. |
| `Chilled` | Chill effects. |
| `Corruption` | Corrupted blood style effects. |
| `Cursed` | Curse effects. |
| `Exposed` | Elemental exposure effects. |
| `Frozen` | Freeze effects. |
| `Frozen Or Chilled` | Freeze or chill effects. |
| `Poisoned` | Poison and chaos ground effects. |
| `Shocked` | Shock effects. |
| `Unable To Recover` | Effects that prevent or interfere with recovery. |

The contents of this file are data-driven. If GGG changes internal buff names, update the JSON.

## 9. Plugin Settings

Top-level settings:

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `Enable` | `true` | Global plugin enable toggle. |
| `ShowDebugWindow` | `false` | Shows action history and current pause state. |
| `InspectState` | `false` | Opens ExileCore inspection for the current `RuleState`. |
| `ShowControllerState` | `false` | Opens a controller state window with XInput buttons and detected skill-bar texture bindings. |
| `DumpState` | n/a | Copies serialized `RuleState` JSON to the clipboard. |
| `ImageDirectory` | `textures/ReAgent` | Directory for `DisplayGraphicSideEffect` images, relative to the ExileCore directory. |

`ReAgent Settings` submenu:

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `GlobalKeyPressCooldown` | `200` | Minimum milliseconds between queued key presses. |
| `MaximumMonsterRange` | `200` | Maximum monster distance collected for nearby monster helpers. |
| `HistorySecondsToKeep` | `60` | How long the debug action history keeps entries. |
| `EnableInEscapeState` | `false` | Allows actions while escape state is active. |
| `KeepEnableTogglesOnASingleLine` | `true` | Keeps group enable/area toggles on one row when possible. |
| `ColorEnableToggles` | `true` | Colors group enable/area toggle labels. |
| `EnableVerticalGroupTabs` | `true` | Uses the vertical group tab layout. |
| `VerticalTabContainerWidth` | `150` | Width of the vertical group tab column. |
| `IgnoreGracePeriod` | `false` | Allows actions during `grace_period`. |

`ReAgentAuras` settings:

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `Unlocked` | `true` | Allows ReAgentAura icons to be dragged. |
| `Poll Interval Ms` | `100` | Poll interval for ReAgentAura condition evaluation. |
| `Enable Extraction` | `true` | Enables extraction controls for frames and icons. |
| `Content.ggpk Path` | empty | Path to POE2 standalone `Content.ggpk`. |

## 10. `SideEffectApplicationResult`

Every side effect returns a `SideEffectApplicationResult` when ReAgent attempts to apply it.

| Value | Description |
| ----- | ----------- |
| `UnableToApply` | The side effect is not complete yet or cannot currently be applied. It remains pending. |
| `AppliedUnique` | The side effect applied and changed state. This is added to action history. |
| `AppliedDuplicate` | The side effect was already in the requested state. It is treated as successfully applied but not logged as a new unique action. |

Delayed side effects and key presses commonly return `UnableToApply` until they can finish.
