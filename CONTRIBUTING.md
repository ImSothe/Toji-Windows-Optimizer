# Contributing

Issues and pull requests are welcome.

## Adding or changing a tweak

Tweaks are data, not code: edit `src/SotheOptimizer/data/tweaks.json`. A tweak is a list of operations
(`reg`, `service`, `command`, `appx`). Rules a pull request has to respect:

- **It must be undoable.** `reg` and `service` operations restore their previous value automatically. A `command`
  operation needs a `revert` that restores the *previous state*, not a hard-coded default.
- **Say what it does and what it costs.** Fill `desc` and `warning`. If you have no measurement showing a benefit,
  do not claim one.
- **Shared keys are fine** (reverting is LIFO), but add a test if two tweaks write the same key.
- Risk level: `safe` changes appearance or privacy toggles; `moderate` changes behaviour with a performance or power
  trade-off; `high` weakens a protection; `extreme` is hidden behind Advanced Mode.

## Translations

English is the default language and Spanish is included.

- **Tweaks, apps, features and profiles** (`data/*.json`): the main fields (`name`, `desc`, `warning`) are English; the
  translation goes in the item's `es` block. Every item must have it; a test checks that.
- **Everything else** (buttons, dialogs, messages): one table, `src/SotheOptimizer/Strings.cs`, with the English text and
  the Spanish text under the same key. Use `Loc.T("key")` in code and `{Binding [key], Source={x:Static local:Loc.Instance}}`
  in XAML. Tests fail if a key is missing in either language, if `{0}` placeholders differ, or if a key is used but not defined.
- Technical messages (log lines, engine errors) are English only and are not translated.

## Tests

```powershell
dotnet test tests/SotheOptimizer.Tests -c Release
```

Registry tests must only use keys under `HKCU\Software\TojiTests\<guid>` and clean up after themselves.

## Style

Match the surrounding code. Comments in the existing code are in Spanish; English comments in new code are fine.
