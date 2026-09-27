# 0003: Switch an idle wireless pad off by dropping its Bluetooth link
**Status:** Accepted · **Date:** 2026-09-27

## Context
A wireless PlayStation pad has no idle timer of its own on a PC. Steam normally switches it off
after 15 minutes unused. With "Use HidHide" on, the app hides the pad from every other program,
Steam included, and without a virtual pad Steam sees nothing, so the pad stayed on until its
battery ran out.

## Options
- **Allow-list Steam in HidHide**: Steam would see the pad again, which is exactly what HidHide is
  there to prevent (the Guide button opening Big Picture).
- **Send a power-off command to the pad**: PlayStation pads have no documented HID command for it.
- **Drop the pad's Bluetooth link with `IOCTL_BTH_DISCONNECT_DEVICE`** on the radio it's connected
  through: the pad switches itself off when the link goes. Tested on the user's DualSense from an
  unelevated process: it disconnected and powered off.

## Decision
Disconnect over Bluetooth, from the app, after `ControllerIdleOffMinutes` (default 15, 0 = never)
without anything the app would act on: a button, a stick past its deadzone, a trigger, or a finger
on the touchpad. The pad's address is SDL's serial for it. The wait resets whenever a fullscreen
game has the pad, since its use isn't visible then, and on every reconnect.

## Consequences
- Needs no elevation and no extra package: `BluetoothFindFirstRadio` plus one `DeviceIoControl`.
- Works for every pad whose serial SDL reports as a Bluetooth address (PlayStation pads). Others
  simply never power off, as before.
- A pad on USB can't be switched off; the failed disconnect is logged once and retried only after
  the pad is used again.
- Steam may also switch the pad off when it can see it (HidHide off); both doing it is harmless.
