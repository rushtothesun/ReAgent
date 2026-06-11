# ReAgent

## Changes in this fork

- Added controller-aware rule APIs:
  - `State.IsUsingController`
  - `State.Controller.IsConnected`
  - `State.Controller.IsPressed(ControllerKey)`
  - `State.Controller.LeftTriggerPressure`
  - `State.Controller.RightTriggerPressure`
- Added controller skill binding lookup APIs:
  - `State.Controller.Skills.PrimaryKeyForTexture(...)`
  - `State.Controller.Skills.SecondaryKeyForTexture(...)`
  - `State.Controller.Skills.HasPrimaryTexture(...)`
  - `State.Controller.Skills.HasSecondaryTexture(...)`
- Added a `ShowControllerState` debug window for live controller button state, trigger pressure, primary/secondary skill bar bindings, and copyable skill icon texture names.
- Added input-specific keybinds for normal `Key` rules:
  - `Keyboard` binding uses `KeyV2`.
  - `Controller` binding uses `ControllerKeyV2`.
- Changed new key rules to default to `None` instead of `D0`.

Use `Show Controller State` to copy the DDS of a skill for use in a rule. For example, the following will attempt to locate the Snipe skill and find its binding on the primary skill bar:

```csharp
var snipeKey = State.Controller.Skills.PrimaryKeyForTexture("RangerSnipeShotArrow.dds");
```
