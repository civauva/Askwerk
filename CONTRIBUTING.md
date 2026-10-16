# Contributing to Askwerk

Thank you for helping. Askwerk has one rulebook for humans and coding agents alike: [`agents/`](agents/AGENTS.md). This page is the short version for contributors.

## Before you write code
1. **Open or find an issue** on GitHub. Bugs: what happened, what you expected, how to reproduce. Ideas: the problem you want solved, not only the solution.
2. A maintainer **accepts** the issue and links it to the internal work item (`AWK-…`). Please wait for that before starting larger work.
3. **Visuals first.** If your change adds or changes a screen, a data-model entity, a configuration surface or a Public API endpoint, post a wireframe (or the entity diagram, or the API contract sketch) in the issue or the pull request first. A maintainer approves it before code review starts. See [`agents/workflow.md`](agents/workflow.md).

## How we build
- **Pure TDD.** No production code without a failing test that requires it: red → green → refactor, visible in your commits (`test:` → `feat:`/`fix:` → `refactor:`). Rules per layer in [`agents/testing.md`](agents/testing.md).
- **No hard dependencies.** Prefer what .NET and the browser provide. A new library needs a justification in `agents/decisions.md`; see [`agents/coding-standards.md`](agents/coding-standards.md).
- **Readable code.** Formatting is enforced by `.editorconfig`, `dotnet format` and Prettier.

## Branches, commits, pull requests
- Branch: `<type>/<work-item-key>-<slug>`, for example `feat/AWK-42-publish-to-domain`.
- Commits: [Conventional Commits](https://www.conventionalcommits.org/) with the work item key, for example `feat(publishing): one-click domain publish (AWK-42)`.
- **Sign off every commit** (`git commit -s`). This certifies the [Developer Certificate of Origin](https://developercertificate.org/): you wrote the change or have the right to submit it under the folder's licence ([LICENSING.md](LICENSING.md)). Pull requests with unsigned commits cannot be merged.
- One work item per pull request; fill in the template. Pull requests are squash-merged; until then keep your commit history as it is, because it shows the TDD cycle.

## Run the tests
```bash
bin/test-changed.sh        # while building: only the layers you touched, in parallel
bin/test-all.sh            # backend and frontend in parallel, then Playwright against dotnet run
bin/test-all.sh --full     # everything CI runs: also builds the image once, Playwright and smoke test in Docker
bin/test-backend.sh        # .NET
bin/test-frontend.sh       # TypeScript
bin/test-e2e.sh            # Playwright against dotnet run (PostgreSQL from ASKWERK_TEST_DB or a container)
bin/test-deploy.sh         # Docker install smoke test
```
Locally Docker is used only for PostgreSQL; with `ASKWERK_TEST_DB` (an owner connection string to a PostgreSQL whose role may create databases and roles) not even that. Only `--full` builds the image.
CI runs exactly these scripts; there are no CI-only steps.

## Behaviour
Be respectful and stay on topic. Maintainers may edit or remove comments and block users who are not.

## Security
Never report a vulnerability in a public issue; see [SECURITY.md](SECURITY.md).
