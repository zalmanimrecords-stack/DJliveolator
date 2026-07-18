# Contributing to Liveolator

Thanks for your interest. A few things to set expectations up front.

## This project is maintained by one person

Liveolator is developed and maintained solely by its author. That shapes how
contributions work:

- **You are welcome to** open an issue, suggest an idea, report a bug, or fork
  the project and build your own version (that is your right under the GPL).
- **Pull requests are reviewed at the maintainer's sole discretion.** There is
  no service-level agreement, no guarantee a PR will be reviewed or merged, and
  no obligation to explain a decision. Every change to this repository is
  approved and merged only by the maintainer (enforced via `CODEOWNERS` and
  branch protection).

Please open an issue to discuss anything non-trivial **before** writing a large
PR, so you don't spend effort on something that won't be merged.

## Building

See the "Getting started" section in [`README.md`](README.md). In short: run
`scripts/fetch-bass.*` to fetch the BASS native libraries, then
`dotnet build Liveolator.sln`. Please run `dotnet test Liveolator.sln` before
submitting a PR.

## Licensing of contributions

By submitting a contribution you agree that it is licensed under the project's
license, the **GNU GPL v3 or later** (see [`LICENSE`](LICENSE)). Do not submit
code you do not have the right to license this way, and do not paste code from
GPL-incompatible or proprietary sources.

Note that the BASS audio libraries are a separate proprietary dependency and are
**not** part of this repository — see the License section of the README.
