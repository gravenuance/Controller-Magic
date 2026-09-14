# Controller Magic

If you know Controller Companion, you'll be familiar with what this does.
It turns an Xbox (or any XInput/SDL-recognized) controller into a mouse, keyboard, and media
remote outside of fullscreen games — a background tray app, not a window you keep open.

![UI](/eg.png)

## Features

- **Mouse control** — left stick moves the cursor, with tunable deadzone, sensitivity, an
  acceleration curve, and a hold-time speed ramp-up. Right stick scrolls (vertical and
  horizontal).
- **On-screen keyboard** — click the left stick to bring up a radial daisywheel keyboard, driven
  entirely by the controller: left stick picks a direction, the shoulder buttons and triggers
  cycle through letters and layers, A commits.
- **Fullscreen-aware** — controller input is normally suppressed while a game has focus (so it
  doesn't fight with in-game controller support), except for an allowed list of apps (browsers,
  media players, Steam, Explorer) where mouse/keyboard control keeps working.
- **Streaming shortcuts** — inside a recognized streaming service (Netflix, Prime Video, Disney+,
  etc.) or Edge, the D-pad becomes arrow keys, the shoulder buttons skip to the next/previous
  episode, and X presses "Skip Intro".
- **Starts with Windows** — on by default, toggleable from Settings.
- **Use HidHide** (optional, off by default) — stops the Guide/Home/Steam button from opening
  Xbox Game Bar or Steam's Big Picture mode, so it's free to be used for something else, and
  stops the left stick/D-pad from also driving Windows' built-in gamepad UI focus navigation
  (which otherwise fights with using the stick as a mouse). Needs two one-time driver downloads
  (HidHide + ViGEmBus, fetched and installed automatically behind a single admin prompt the
  first time it's turned on) and may be blocked by some anti-cheat systems (BattlEye,
  EasyAntiCheat) while it's active.
- **Right-click (or double-click) the tray icon** for Settings, Restart, and Exit.

Deadzones, sensitivity, the acceleration curve, ramp-up time, and both app/service lists are all
adjustable from Settings.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```
dotnet build "Controller Magic.slnx"
```

To produce the distributable, self-contained single-file executable:

```
dotnet publish "Controller Magic/Controller Magic.csproj" -p:PublishProfile=win-x64
```

The result lands in `Controller Magic/bin/Release/net10.0-windows/publish/win-x64/` and needs no
.NET runtime installed on the machine it runs on.

## Running

Run the built (or published) executable directly — it has no window, just a tray icon. A second
launch while it's already running quietly exits instead of opening twice.

## Testing

```
dotnet test "Controller Magic.slnx"
```
