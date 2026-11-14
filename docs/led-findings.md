# LED protocol findings

## Device covered

These findings apply to the three-key, one-knob keypad identifying as USB `1189:8890` and to the stock firmware command currently used by Keypad Layers. Similar-looking macropads and other product IDs may use different firmware and commands.

## Verified controls

Hardware-tested reports for this exact USB ID identify the built-in LED modes as:

| App label | Observed behavior |
| --- | --- |
| Off | LEDs off |
| Mode 1 | Reactive lighting on keypress |
| Mode 2 | Built-in color-cycling effect |

The keypad chooses the behavior and colors for each built-in effect. The verified command selects a mode; it does not carry an RGB value. No brightness or animation-speed control is exposed for this model.

## What is not established

- No verified stock-firmware command selects a custom color or brightness for `1189:8890`.
- The available evidence does not establish per-key RGB control.
- Color and effect controls documented for other CH57x keypad families use different firmware/protocol paths and should not be sent to this device as if they were compatible.
- Replacing or modifying firmware would be a separate hardware project; this app does not flash firmware.

## Sources

- [WonKey protocol evidence: hardware-tested `1189:8890` modes](https://github.com/wimpysworld/WonKey/wiki/protocol#hardware-verification)
- [WonKey lighting controls and limitations](https://github.com/wimpysworld/WonKey/wiki/usage#lighting)
- [CH57x keyboard tool's 8890 LED command](https://github.com/kriomant/ch57x-keyboard-tool/blob/master/src/keyboard/k8890.rs)
- [Mini 3Key Configurator's reverse-engineered notes for the supplied vendor software](https://github.com/EScripts-content/mini-3key-configurator/blob/main/DOCS/PROTOCOLO_HID.md)

The app continues to show the vendor's neutral labels **Mode 1** and **Mode 2**. This note records protocol findings only; it does not change application behavior.
