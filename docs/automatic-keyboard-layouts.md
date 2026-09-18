# Automatic Windows keyboard layouts

GoBoard follows the focused application's full Windows HKL in desktop and VR mode. It now derives printable labels and AltGr behavior from the registered Windows keyboard DLL, instead of maintaining a character table for each language. Input still uses physical scan codes and Windows handles the resulting text, shortcuts, and composition.

## How labels are obtained safely

`WindowsLayoutProvider` resolves the full HKL to its KLID on a fresh, windowless thread. It activates only an already-loaded layout on that thread, reads its name, and restores the thread's previous layout. It does not install input languages or activate a layout on the target application's thread. The provider caches immutable results by full HKL and arrangement setting for the process lifetime; restart GoBoard after replacing a layout DLL.

`KeyboardTables` loads the registry-named `kbd*.dll` from System32 with System32-only dependency resolution. It reads the `KbdLayerDescriptor` export and the x64 `KBDTABLES`, `MODIFIERS`, and character/ligature tables defined by the Windows SDK's `um/kbd.h`. Pointer reads are checked against the DLL image and table scans are bounded. Unsupported table versions, locale flags, lock modes, or missing data cause a fallback. No code calls `ToUnicodeEx` in the production label path, and rendering reads only copied managed data.

The eight layers cover Shift, AltGr (Ctrl+Alt), Caps Lock, and their combinations. Caps behavior comes from each key's attributes, including Caps+AltGr. Dead-key accents and no-output entries remain distinct, and ligature strings preserve their UTF-16 units. AltGr injection and rendering are independent of geometry and language.

The private-desktop experiment was rejected: a query on a different desktop/thread still inherited an accent pending elsewhere. Direct table reads avoid that shared translation state entirely. This supersedes the runtime `ToUnicodeEx` pipeline proposed in the [historical research](automatic-keyboard-layouts-research.md).

## Arrangement and fallback

Automatic geometry has explicit metadata for US, US Dvorak, US International (ANSI), Swedish, UK, German, and legacy French AZERTY (ISO). Other readable layouts use all ISO positions and display “Windows labels; generic ISO arrangement.” The presence of an output at scan `0x56` is never used to infer geometry.

The shared **Keyboard arrangement** setting cycles Auto → ANSI → ISO. Overrides apply to generated layouts, are saved alongside size/sound, and update desktop and VR. Changing the arrangement cancels pending input and discards old hover geometry even when the HKL is unchanged.

If generation is unavailable, the existing static US/Swedish tables remain the fallback. Other unsupported layouts retain usable US labels with a warning; their real HKL is still used for input and change detection. Input errors take precedence over layout notices.

CJK/IME layouts and layouts with additional lock behavior (such as SGCAPS, kana/group modes, or Shift Lock) deliberately use fallback labels. Native desktop IME controls remain available, but JIS geometry, mode-aware IME legends, complex-script shaping/font fallback, and broad multilingual input acceptance are not implemented by this change. A generic arrangement notice is not a claim that every script has been validated.

## Japanese あ/A button

Selecting a Japanese input language adds one **あ/A** key to the right of Space in the shared desktop/VR keyboard. The spacebar shortens to make room; other keys keep their positions. The button sends a balanced `VK_KANJI` input-mode stroke to the current foreground target. It toggles Japanese/Latin input inside the existing Japanese IME and does not select a different Windows input language. The caption is fixed, so external IME mode changes cannot desynchronize a locally guessed state.

The action does not repeat when held. Virtual one-shot/locked modifiers are preserved for the next typed key and are not applied to the toggle. If physical modifiers are held, the action asks for their release instead of sending a modified mode key. The あ glyph uses a matching installed font. US fallback legends and the punctuation warning remain; this is not a JIS or direct-Kana layout.

The user reported that the Japanese composition/candidate workflow worked in their manual test. `--ime-check` additionally passed with the installed Japanese IME: actual `ka` → `か` → `ka`, no repeat while holding the button, correct behavior after an external mode change, unchanged target focus/HKL, and no held keys. The check temporarily focuses a disposable Rich Edit window, uses an already-loaded Japanese layout, configures only its disposable IME context, and restores the previous window/layout afterward. The new button still needs in-headset acceptance.

```powershell
dotnet run --project src/GoBoard.App -c Release -- --ime-check
dotnet run --project src/GoBoard.App -c Release -- --render .runtime/japanese.png --layout ja
```

## Verification

- Regression tests compare every US/Swedish printable key in all eight layers with the existing fixtures, and check German QWERTZ, French AZERTY, UK punctuation, and US International dead keys/AltGr/Caps+AltGr against explicit expected values.
- Tests cover full-HKL cache identity, preservation of caller language/focus, geometry override, balanced AltGr strokes, cancellation on arrangement changes, saved settings, and rendering of the new layouts in all eight states.
- `--layout-check` uses a disposable foreground Rich Edit window and already-loaded US/Swedish layouts. A cold provider lookup and reads of German/French/US International tables while a Swedish accent is pending must produce clean labels and preserve the subsequent `é`. It also checks actual punctuation, AltGr text, layout switches, and released modifiers.
- `--settings-input-check` checks the arrangement button and persistence using a disposable settings file.
- German/French/US International previews have been visually checked. Actual typing in those three layouts, additional scripts, and headset acceptance still need validation on appropriately configured input languages; this work did not add languages to Windows.

Generate a preview without changing Windows' active input language:

```powershell
dotnet run --project src/GoBoard.App -c Release -- --render .runtime/german.png --layout de
dotnet run --project src/GoBoard.App -c Release -- --render .runtime/french.png --layout fr
dotnet run --project src/GoBoard.App -c Release -- --render .runtime/international.png --layout us-intl --state altgr
```

`--layout` also accepts `us`, `sv`, `uk`, `ja` (Japanese fallback with the IME button), or an eight-digit registered KLID. Unsupported table formats fail explicitly in previews. Existing input checks remain guarded against focus leaving their disposable window.

## References

- [Microsoft keyboard layout driver samples](https://github.com/microsoft/Windows-driver-samples/tree/main/input/layout) and the locally installed Windows SDK 10.0.26100.0 `um/kbd.h` describe the native tables.
- [ToUnicodeEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-tounicodeex) documents the shared keyboard translation state and dead-key interactions.
- [ActivateKeyboardLayout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-activatekeyboardlayout) and [GetKeyboardLayoutName](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayoutnamew) provide full layout identity without deriving a variant from language bits.
