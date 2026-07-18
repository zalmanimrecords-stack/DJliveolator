# Contributing to Liveolator

Thanks for being here — genuinely. Liveolator is an open-source DJ + VJ instrument,
and it gets better every time someone reports a bug, tries it on their machine, or
sends a fix. This guide explains how the project works and where your help lands best.

## The quickest ways to help

You don't need to write C# to make a real difference:

- **Try it and tell us what broke.** Bug reports with steps to reproduce are gold —
  especially on **macOS**, which is the platform we most need real-world testing on.
- **Suggest an idea** or vote on existing ones in
  [Issues](https://github.com/zalmanimrecords-stack/DJliveolator/issues).
- **Improve the docs** — a confusing sentence you fixed is a contribution.
- **Pick up a [`good first issue`](https://github.com/zalmanimrecords-stack/DJliveolator/labels/good%20first%20issue)**
  — these are scoped to be approachable without deep knowledge of the codebase.

Areas that are wide open right now: **keylock**, the **VJ authoring UI**, and
**cross-platform (macOS) packaging**. If any of those excite you, say hi in an issue.

## How this project is run

Liveolator is maintained by one person, so a little transparency up front keeps
everyone's time well spent:

- **The maintainer has the final say on what gets merged.** Every change is approved
  through `CODEOWNERS` and branch protection. This keeps the architecture coherent
  and the shared beat clock, the action-dispatcher seam, and the pure-`Core`
  boundary intact — the things that make the project what it is.
- **For anything non-trivial, open an issue first.** A quick discussion before you
  write a large PR means you won't pour effort into something that doesn't fit the
  direction — and it gives us a chance to point you at the right seam.
- **Small, focused PRs get reviewed fastest.** A tight fix with a test attached is
  far easier to say yes to than a sprawling one.
- Because this is a solo-maintained project, there's no review SLA — but every issue
  and PR is read, and thoughtful contributions are always appreciated.

None of this is meant to discourage you. It's the opposite: knowing how decisions
get made is what lets you contribute with confidence.

## Building and testing

See **Getting started** in [`README.md`](README.md). In short:

```sh
pwsh scripts/fetch-bass.ps1          # fetch the BASS native libraries (Windows)
./scripts/fetch-bass.sh              # or macOS / Linux
dotnet build Liveolator.sln
dotnet test Liveolator.sln           # please run this before opening a PR
```

`Liveolator.Core` is pure C# with no UI and no native dependencies, so its tests run
anywhere without hardware. New logic in `Core` should come with tests.

**Enable the git hooks once after cloning** — a secret guard that blocks committing or
pushing deploy hosts, private keys, and tokens:

```sh
sh scripts/install-hooks.sh          # macOS / Linux / Git Bash
pwsh scripts/install-hooks.ps1       # Windows / PowerShell
```

Never hardcode deploy targets or credentials in source; the release scripts read them
from `LIVEOLATOR_VPS_*` environment variables.

## Coding conventions

- **Match the surrounding code.** Naming, file size, and comment density should read
  like the files next to yours.
- **Keep the seams intact.** Inputs emit `PerformanceAction`s; engines are driven
  through the dispatcher, never called directly. UI, business logic, data access, and
  native bindings stay in separate layers.
- **Small, single-responsibility files.** Comment the *why*, not the *what*.

## Licensing of contributions

By submitting a contribution you agree that it is licensed under the project's
license, the **GNU GPL v3 or later** (see [`LICENSE`](LICENSE)). Do not submit code
you don't have the right to license this way, and do not paste code from
GPL-incompatible or proprietary sources.

The BASS audio libraries are a separate proprietary dependency and are **not** part
of this repository — see the License section of the [`README.md`](README.md).

## Code of Conduct

Participation in this project is covered by our [Code of Conduct](CODE_OF_CONDUCT.md).
Be kind, be constructive.
