# Contributing

Thanks for helping! / Спасибо за помощь! Issues and pull requests are welcome in English or Russian.

## Reporting a bug

Use the **Bug report** template. Include the Pixie Privacy Pro version (window footer), Windows version, what you did,
what you expected, and the relevant lines from the journal (`%LOCALAPPDATA%\PixiePrivacyPro\events.log` or *Log → Open file*).

## Code

- C# 5 on .NET Framework 4.8, compiled by the `csc.exe` that ships with Windows — no Visual Studio or NuGet needed.
  See *Build from source* in the [README](README.md).
- Run `.\build.ps1 -Test` before sending a change; for UI changes also look at `tests\bin\UiShots.exe` output in both themes and languages.
- Sources are UTF-8 with BOM and CRLF line endings. Interface text is written in Russian and wrapped in `L.T("…")`;
  add the English text to the dictionary in `src/Lang.cs`.
- Keep the privacy rules strict: anything that can open the lens or the microphone needs a clear reason.
- By contributing you agree that your work is licensed under GPL-3.0-or-later.
