# TODO

- [x] Add a settings program accessible on both the desktop and the SteamVR dashboard, with controls for keyboard size and sound.
- [x] Don't completely disable the keyboard on unknown input language. Try to keep what we know as default
- [x] Make sure keys are always sent, even when no textbox is selected. For instance, (double) pressing the windows key while the settings window is open, should open the windows menu.
- [x] Spacing looks odd; spacing between keys is smaller than above/below. Let's make it consistent. Prefer smaller margins
- [x] Implement automatic Windows keyboard layouts (see [implementation and validation](docs/automatic-keyboard-layouts.md)).
- [ ] Auto-hide/show when steamvr native keyboard is toggled
- [ ] Investigate why layout doesn't switch in notepad. Seems to work flawlessly elsewhere. In notepad, even with swedish selected, US layout is shown. Typing still uses swedish layout, so layout is misleading. Switching to a different app changes the layout to be correct, and switching back to notepad makes it US again.
- [ ] "Programmable keys" in a little optional side extension. Think macro keys on a physical keyboard. Desired keys/presets:
  - [ ] Ctrl-Win-Left/Right for moving between displays
  - [ ] Play/Pause/Next/Prev media keys
  - [ ] Win-tab for multitasking view
  - [ ] Win-space for language switching
  - [ ] Win-H for dictation
- [ ] Add a SteamVR application manifest to enable autostart.
- [ ] Add reset button to keyboard that moves it back.
- [ ] Validate actual German/French/US International typing and extend automatic layout support to complex scripts, additional lock modes, and IME/JIS arrangements.
- [ ] Maybe: Numpad
