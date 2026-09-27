# 0002: Move the cursor with SetCursorPos, keep the display awake separately
**Status:** Accepted · **Date:** 2026-09-27

## Context
The stick and touchpad move the cursor a few pixels per 8 ms tick, each step computed from where
the cursor is now. 1.9.0 switched these moves from `SetCursorPos` to one
`SendInput(MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK)` event, so that driving the cursor counts as
input and the display doesn't dim or sleep. Relative `SendInput` moves were ruled out because
pointer speed and "Enhance pointer precision" would reshape the stick's own curve.

Absolute moves assumed Windows maps a normalised coordinate back to `floor(n * size / 65536)`.
Measured on a 3840x1080 virtual desktop (two 1080p monitors, 100% scaling), a move aimed at pixel
`p` landed on `p - 1` about 75% of the time on both axes, and no single formula fitted the mix. As
each step starts from where the last one landed, the error built up: a slight push right
(`dx = 1, dy = 0`) moved the cursor upwards.

## Options
- **Absolute `SendInput` with a corrected inverse**: keeps counting as input, but depends on an
  undocumented mapping that didn't match any formula here and may differ with DPI and monitor
  layouts.
- **Absolute `SendInput`, stepping from the app's own intended position**: the error no longer
  builds up, but the cursor can sit a pixel off, and telling the app's landings apart from the
  real mouse needs a tolerance that mixed-DPI setups could exceed.
- **Zero-distance `SendInput` for input, `SetCursorPos` for position**: a probe could not show a
  0,0 move reaching Windows as input at all.
- **`SetCursorPos`, plus `SetThreadExecutionState(ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)`**:
  exact on every layout; keeps the display and system awake while the stick moves the cursor.

## Decision
`SetCursorPos` for the exact pixel, then a one-off `SetThreadExecutionState` (no `ES_CONTINUOUS`,
so nothing stays held when the stick stops). Exact placement on any layout outweighs counting as
keyboard or mouse input.

## Consequences
- Slight pushes keep their direction; no rounding error builds up.
- Moves are no longer input to Windows: `GetLastInputInfo` doesn't see them, so a screensaver or a
  chat app's "Away" status may still kick in while only the stick is used. The display itself
  doesn't dim or sleep.
- `SetCursorPos` isn't subject to UIPI, so the fallback for an elevated window in front is gone.
- Revisit if Windows documents the absolute mapping, or if "Away" status becomes a complaint.
