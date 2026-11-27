# Contributing

Fork this repository, create a branch and open a pull request into `main`. Describe what changed and how you tested it. Include the exact commit you tested. Maintainers handle merges and releases. The [review policy](AGENTS.md#independent-review) applies to code and documentation.

## Claim an issue

Before starting a fix, check the issue's assignee and recent comments. If nobody has claimed it, comment that you're working on it and briefly describe your approach. If someone is already working on it, coordinate with them first.

When your PR resolves a public issue in this repository, put `Fixes #123` in the PR description, replacing 123 with the issue number. GitHub closes the issue when the PR merges into `main`. For partial progress, use `Refs #123` to keep the issue open.

## Build and test

Use Windows x64 with the .NET 8 SDK and Windows Desktop targeting support. Restore uses public NuGet packages. Run these commands from the root of a fresh checkout. The tests use synthetic data and require no game installation.

```powershell
dotnet build src/RealmOfThronesHost/RealmOfThronesHost.csproj -c Release
dotnet build src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj -c Release
dotnet test tests/RoT.Contributor.Tests/RoT.Contributor.Tests.csproj -c Release
```

These are unsigned development builds without the game modules or Coop archives used to host and join. Use the [official release](https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest) to play. It targets Steam Bannerlord 1.4.8 with War Sails, the full Nexus RoT 8.1.2 package (modules 8.1.8, map 8.1.8.2), and RoT Coop 0.1.8+01846e13. See the [player instructions](Coodsz-RealmOfThrones-Instructions.txt) for dependencies.

## How PRs are reviewed

Keep each PR focused on one change. This repository accepts changes to the RoT tool source and tests as well as the player guide. For tool code, follow the [shared code style guidelines](https://github.com/Coodsz/Coodsz-Coop-Tools/blob/master/CONTRIBUTING.md#how-the-code-should-look) and use this repository's build and test commands above. Keep changes to player or host behavior in draft until tested in game.

- For code changes, build both tools and run the tests above. State what you could verify and what still needs a player or host test.
- Add a regression test for a behavior fix. Keep new UI text in all nine languages under `src/BannerlordCoopLauncher.Core/Localization/Languages`.
- Preserve file encodings and CRLF. Leave generated compatibility records to the existing bot.
- Keep game and mod assets, binary payloads, private logs, credentials and signing files out of commits.
- Submit your original code under the [MIT license](LICENSE.txt). Keep third-party notices intact and identify the license of any proposed third-party material.

For documentation-only edits, check the wording and links. These edits do not need a game test.

## Report a problem

For a tool bug, describe the steps, expected result and actual result. To share logs privately, use **Send my logs** on the released Client Launcher's **Logs and help** page or the Server Tool's **Logs** page. Share only the report code. Use the [mod compatibility form](https://github.com/Coodsz/Coodsz-RealmOfThrones/issues/new?template=mod-compat.yml) for mods that work or break.

## Source and artwork

The MIT license covers Coodsz's contributor source, tests and the geometric artwork generated with `python tools/make-contributor-art.py`. Dependencies, adapted code, the existing compatibility records and third-party materials retain their own licenses. See [third-party notices](THIRD-PARTY-NOTICES.md). The ModderLords-derived SettingsWire.cs and the material identified in the notices retain their stated terms, including PolyForm Noncommercial where applicable. The existing player download image and previously distributed binaries are outside the source grant. See the [official binary license](licenses/Official-binaries.txt).

The RoT maintainer checks merged PRs for shared-code changes, ports applicable fixes into the shared tools repository and records the paired commits before release.
