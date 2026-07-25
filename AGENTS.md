# Repository guidance

This file applies to the whole Sharlayan.Lite repository.

## Repository identity

- This checkout is the fork `sappho192/Sharlayan.Lite`.
- `origin` is `sappho192/Sharlayan.Lite`; `upstream` is `FFXIVAPP/sharlayan`.
- The default and release branch is `min-chat`, not upstream's `main`.
- Always pass `--repo sappho192/Sharlayan.Lite` to `gh`. Repository auto-detection may resolve to
  upstream and show or dispatch the wrong workflow.
- Keep commits scoped and stage explicit paths. Preserve unrelated user changes and untracked files.

## Supported package baseline

- Current source package version: `9.1.4`.
- Latest published package version: `9.1.4`.
- Target frameworks: `net462;net48;net6.0;net7.0;net8.0;net10.0`.
- File version: `9.1.4.0`.
- Assembly version remains `8.0.0.0` for binary compatibility.
- Current embedded Hermes revision:
  `sha256:419248bf2ef93aa64e72723ea9e97d5503163178dab63e90a8155b359ebcf96d`.

## Hermes v2 invariants

- Resource priority is remote → verified cache → embedded.
- Validate schema, compatibility, revision hash, validation metadata, and every required layout
  before applying a manifest.
- CHATLOG, LastTalk, and currentTalk must come from the same manifest revision and be applied
  atomically. Never mix remote signatures with embedded structures.
- Freeze the selected revision for a handler lifetime. Do not hot-swap part of a running handler.
- Embedded production bytes must match the live-verified Hermes immutable object exactly. Preserve
  LF/no-BOM rules and never embed a candidate.
- Remote failure must fall back to cache/embedded rather than fail handler construction.

## Talk semantics

- LastTalk is the last committed standard Talk value, not the currently visible addon.
- LastTalk UTF-8 length is `BufUsed - 1`; `StringLength(+0x18)` may be zero for valid live values.
- Validate pointer, bounded size, null terminator, strict UTF-8, and a stable before/after snapshot.
- CurrentTalk comes from the visible, ready `Talk` addon.
- The observed contract is text at `AtkValues[0]`, speaker at `AtkValues[1]`, and
  `ManagedString(0x28)` values.
- `GetTalk()` is current-first and only falls back to LastTalk when current is absent or temporarily
  unavailable.
- Shared logs should omit actual names/text by default. Use `--print-talk` only with explicit
  permission in a local diagnostic session.

## Build and validation

Run:

```powershell
dotnet run --project tools/Sharlayan.ChatResources.Generator/Sharlayan.ChatResources.Generator.csproj --configuration Release --no-launch-profile
git diff --exit-code -- Sharlayan/Resources/GeneratedChatResources.g.cs
dotnet restore Sharlayan.sln
dotnet build Sharlayan.sln --configuration Release --no-restore --nologo
dotnet test Sharlayan.Tests/Sharlayan.Tests.csproj --configuration Release --no-build --no-restore --nologo
dotnet pack Sharlayan/Sharlayan.csproj --configuration Release --no-build --no-restore --nologo --output artifacts/packages -p:PackageVersion=<version>
.\tools\Verify-Package.ps1 -PackageDirectory .\artifacts\packages -ExpectedPackageVersion <version> -ExpectedRepositoryCommit <final-verifier-sha>
.\tools\Verify-PackageConsumer.ps1 -PackageDirectory .\artifacts\packages -ExpectedPackageVersion <version> -ExpectedRepositoryCommit <final-verifier-sha>
```

Live smoke is manual because it requires an interactive game session and a GPU-capable Windows
instance. Build and unit tests are not substitutes for live attach, current Talk, CHATLOG polling,
or wrap evidence.

The 9.1.2 release used an explicit one-time waiver for incomplete extended evidence so acquaintances
could test it. The approximately 420-second run was not a 10-minute PASS, and 1,000-entry/wrap,
explicit channel fixtures, elevated attach, and upstream comparison remained incomplete. Do not
carry this waiver into another release.

The 9.1.4 release used a separate, version-scoped user decision after 420.1 seconds, 2,723 entries,
and 3 observed cursor wraps passed without `Invalid chat entry size`, `OnException`, or stderr.
This exceeded the prior failure point and the 1,000-entry/wrap threshold, but it was not a 10-minute
PASS. Explicit user-action channel fixtures, elevated attach, and upstream comparison also remain
incomplete. Do not carry the 9.1.4 decision into another release.

## NuGet release

- Workflow: `.github/workflows/release.yml`.
- First run with `publish=false`; inspect build, tests, package verification, and artifact.
- Then run the same version with `publish=true`.
- Protected environment: `production`.
- Authentication uses GitHub OIDC and `NuGet/login`; do not restore a stored long-lived API key.
- Only the publish job gets `id-token: write` and environment approval.
- Never use `--skip-duplicate` to hide a stable-version collision. NuGet versions are immutable;
  confirm the version is absent before publishing.
- After push, wait for flat-container and registration indexing, then restore the exact version with
  a fresh cache and build a consumer project.
- Package 9.1.2 was published by Actions run
  `https://github.com/sappho192/Sharlayan.Lite/actions/runs/30161079520`.
- Package 9.1.3 was accepted and indexed by NuGet from Actions run
  `https://github.com/sappho192/Sharlayan.Lite/actions/runs/30165039063`, but that run concluded as
  failed after publication. `dotnet nuget push` automatically pushed the sibling `.snupkg` with the
  `.nupkg`, and the workflow then pushed the same `.snupkg` explicitly and received HTTP 409. Never
  rerun 9.1.3 or use `--skip-duplicate`: it is already immutable and public.
- Package 9.1.4 was published successfully by Actions run
  `https://github.com/sappho192/Sharlayan.Lite/actions/runs/30166550163`. Its `.nupkg` push uses
  `--no-symbols`, and the verified `.snupkg` is submitted separately exactly once.
- Packages from 9.1.3 onward include an embedded README.
- Do not copy Environment input identifiers, reviewer identities, local user paths, process IDs,
  player names, user-generated chat, NPC names, or raw NPC dialogue into public documentation.
  Exact Talk strings may be viewed transiently in a local agent diagnostic session, but retain only
  match status, source, visibility, and lengths in committed artifacts. Inspect current configuration
  without exposing values:

```powershell
rg -n 'environment:|id-token:|secrets\.' .github/workflows/release.yml
gh secret list --repo sappho192/Sharlayan.Lite --env production
gh api repos/sappho192/Sharlayan.Lite/environments/production
gh api repos/sappho192/Sharlayan.Lite/environments/production/deployment-branch-policies
```

Read `docs/2026-07-25/2026-07-25-hermes-v2-and-nuget-release.md` before the next release.
For the 9.1.3 package-contract fix, also read
`docs/2026-07-26/2026-07-26-sharlayan-9.1.3-release-preparation.md`.
For the 9.1.4 CHATLOG reset fix and release evidence, read
`docs/2026-07-26/2026-07-26-sharlayan-9.1.4-chatlog-reset-release.md`.
