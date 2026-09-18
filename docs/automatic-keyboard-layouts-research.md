# Automatic keyboard layouts for GoBoard

> Historical research note: the implementation has since completed static, Windows-derived eight-state legend tables and full 79-key US/80-key Swedish geometry. Runtime `ToUnicodeEx` legend lookup was rejected because it can disturb a pending global dead-key accent. Current behavior is documented in `README.md` and `docs/development.md`.

Research date: 2026-09-18. This is a proposal, not an implemented feature or an expansion of the confirmed product scope.

## Recommendation

Use Windows as the source of truth for ordinary installed keyboard mappings, with a small collection of physical geometry templates. Leave IME composition and candidate UI in the desktop application: the user can point and click native Windows suggestions through the SteamVR desktop. Add an external keyboard engine only if GoBoard needs languages or input methods that the user's Windows installation does not provide.

This avoids maintaining a character table for every language. It does not eliminate the need for geometry choices, modifier handling, script rendering, and input-method testing. A language is not a unique layout: regional layouts, phonetic layouts, and user preferences can differ within one language.

## What the project already has

- `WindowsKeyboard.Foreground()` resolves the foreground window's thread and full HKL. It already follows the appropriate application's layout rather than GoBoard's own thread.
- `WindowsKeyboard` sends scan codes through `SendInput` and tracks key ownership.
- `KeyboardOverlay.BeginFrame()` cancels held input when the target/layout changes.
- `KeyboardLayout` still hardcodes a reduced Latin QWERTY keyboard. It omits number and punctuation rows, Swedish extra letters, and most modifiers.
- `Panel` hardcodes “US English” and uses `ToUpperInvariant()` for capitals. This must become layout-derived output; Shift and Caps Lock cannot generally be reduced to uppercasing text.
- The existing technical design already proposes Windows-derived legends. This research supports generalizing that design beyond its initial Swedish/US scope.

## Windows generation pipeline

1. Observe the foreground application's full HKL. Compare full handles, not just the language bits; variants can share a language. Since GoBoard does not own the target's message loop, continue checking the foreground target directly. Microsoft documents the thread argument and dynamic layout changes in [GetKeyboardLayout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout).
2. Start with stable physical key positions and scan codes from a geometry template. Resolve each scan code to a virtual key with `MapVirtualKeyEx(..., MAPVK_VSC_TO_VK_EX, hkl)`. Its character mapping mode is insufficient for legends; see [MapVirtualKeyExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeyexw).
3. Generate legends with `ToUnicodeEx`, an explicit 256-byte modifier state, and flag `0x4` to avoid changing keyboard state. Query base, Shift, Caps Lock, Shift+Caps Lock, AltGr and relevant combinations. Keep strings, dead-key status, and no-output results separately. Preserve the returned UTF-16 length; a key can produce multiple code units. Use explicit Unicode marshaling in C#. The non-mutating flag requires Windows 10 1607 or newer. [ToUnicodeEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-tounicodeex).
4. Cache generated legends by full HKL and modifier state. Rebuild on layout changes and select layers as modifiers change. Do not query every key on every VR frame. Keep special-key labels separate from text mappings.
5. Continue sending physical key events so the target handles shortcuts and composition. Add explicit extended-key identity throughout injection, ownership, repeat, and release. In particular, right Alt is an extended key; AltGr display queries and actual right-Alt injection need separate treatment. [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput).

For AltGr preview, test the Ctrl+Alt state and side-specific modifier bits against each layout; do not assume all Ctrl+Alt combinations are printable. Physical modifiers and GoBoard-owned modifiers must be reconciled. Existing ownership protection only specially handles the current left Shift key.

Windows input still goes to the foreground input stream. `SendInput` does not target an arbitrary window and may be blocked by integrity-level restrictions. Retain immediate target validation and cancellation. [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

## Local evidence

A standalone read-only probe enumerated loaded HKLs with [GetKeyboardLayoutList](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayoutlist), then queried seven scan codes in four states. It did not send input, load layouts, activate layouts, or change language settings.

Selected successful results:

| Physical scan | US base / Shift | Swedish base / Shift | Swedish AltGr |
| --- | --- | --- | --- |
| `0x1A` | `[` / `{` | `å` / `Å` | no output |
| `0x27` | `;` / `:` | `ö` / `Ö` | no output |
| `0x28` | `'` / `"` | `ä` / `Ä` | no output |
| `0x03` | `2` / `@` | `2` / `"` | `@` |
| `0x56` | `\` / `\|` | `<` / `>` | `\|` |

Swedish `0x0D` was reported as a dead key for both its acute-accent base legend and grave-accent Shift legend. The loaded Japanese HKL returned Latin base mappings in this probe. That is not evidence of working Japanese composition or a way to determine its active conversion mode.

The initial probe used incorrect character-buffer marshaling and crashed its helper processes. After correcting the declaration to explicit Unicode with a `StringBuilder` buffer, the complete probe exited successfully. No application implementation was changed. These results establish mapping lookup feasibility only; they do not validate typing, pending-dead-key interactions, IME operation, or headset behavior.

## Geometry and text rendering remain separate

The translation APIs provide mappings, not a complete visual arrangement. Build ANSI and ISO templates first, then JIS and other templates as support is validated. Provide a geometry override and keep template metadata independent of language names. Do not infer ANSI/ISO merely from whether `0x56` has output: the local US layout also returned a mapping for that position.

For VR, these templates can define key positions and widths in units while one layout algorithm fits them to the panel. Unknown arrangements can use an explicitly generic presentation, provided all required keys are reachable. A three-row Latin letter grid cannot serve as the universal template.

Replace character-length heuristics with explicit key roles. Use font fallback and shaping for scripts that require them; SkiaSharp provides [HarfBuzz integration](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.harfbuzz). Shaping is only one part of rendering: mixed-direction text also needs bidirectional layout, and missing glyphs need fallback fonts. These are proposed renderer requirements, not capabilities verified in the current panel.

## Native desktop IMEs can remain in charge

Japanese kana/kanji conversion, Chinese input methods, and Korean composition require behavior beyond static key legends. The user's intended workflow already provides access to that behavior: send physical keys to the desktop application, then point and click its native IME suggestions through the SteamVR desktop. GoBoard does not need to implement composition or render a candidate panel.

Use passthrough to the target IME. Include any required keyboard controls in the appropriate template, with desktop mode controls remaining available. Integration checks should cover typing, clicking native candidates, returning to GoBoard, commit/cancellation, and focus preservation. Mode-dependent key legends, such as kana legends, are a separate display concern: a Latin mapping returned by the translation API does not reveal every IME mode.

A custom candidate panel is unnecessary for this scope. For future reference only, Microsoft's [TSF UI-less mode](https://learn.microsoft.com/en-us/windows/win32/tsf/uiless-mode-overview) supports candidate rendering in a participating application; it is not needed for the selected desktop passthrough design.

## External sources compared

| Source | Useful role | Limitation for GoBoard |
| --- | --- | --- |
| Windows APIs | Match the focused app and installed ordinary keyboard layouts | Geometry and complete IME state/UI are separate |
| Keyman | Broader language coverage using existing keyboard definitions and an engine | Requires engine/context integration; importing labels alone loses keyboard behavior |
| Unicode LDML keyboards | Standard interchange model for layers, keys, and transformations | Not a current exhaustive mirror of Windows layouts |
| XKB / xkeyboard-config | Rich layout/variant data, especially for a future Linux backend | Linux definitions are not automatically identical to Windows behavior |

[Keyman](https://keyman.com/en/) advertises support for over 2,500 languages. Its [keyboard repository](https://github.com/keymanapp/keyboards) distinguishes release, legacy, and experimental packages. It is the strongest candidate to investigate for a long-tail language engine; verify the chosen engine API, exact keyboard package license, fonts, and quality status before bundling. [Keyman developer entry point](https://keyman.com/en/developer/).

[Unicode LDML Keyboard 3.0](https://www.unicode.org/reports/tr35/tr35-keyboards.html) includes mappings and transforms, but its rewrite is incompatible with older keyboard files. It explicitly does not unify existing platform layouts. Use it as a format/import option, not as proof of universal layout coverage.

[xkeyboard-config](https://xkeyboard-config.freedesktop.org/doc/config/) separates models, layouts, variants, and options. That separation is useful architecture guidance, but Windows-derived data should remain authoritative for native Windows passthrough.

## Suggested delivery sequence

1. Add a Windows layout provider and complete ANSI/ISO key sets. Generate Swedish and US legends from the same code, including punctuation, AltGr, and dead keys. Keep geometry and legend data separate.
2. Verify more installed layouts: German QWERTZ, French AZERTY, US International, Russian, Arabic, Hebrew, and a representative Indic layout. Test actual input separately from generated labels; support should be based on evidence, not the number of layouts enumerated.
3. Add shaping/fallback and a capability status distinguishing native mappings, unverified arrangements, and IME-dependent input. Test layout switches while keys are held and physical/virtual modifier combinations.
4. Validate Japanese, Chinese, and Korean desktop passthrough: type through GoBoard and point/click native candidates on the desktop. Keep composition and suggestion rendering in Windows/the target application.
5. Only if needed, prototype Keyman as an additional provider with a composition model. Keep its text-output behavior separate from Windows scan-code passthrough.

The immediate next experiment should render the complete US and Swedish keyboard from Windows-generated data and verify typed results in a controlled text field. This is a bounded way to prove that adding ordinary layouts no longer requires writing new character tables.
