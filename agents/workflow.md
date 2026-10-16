# workflow.md — the way work moves

## 1. Work items live in Throughly
- Project management is my.throughly.app, project **Askwerk**, key **`AWK`** (items are `AWK-1`, `AWK-2`, …). Agents use its MCP at my.throughly.app/mcp; the committed `.mcp.json` reads the token from the `THROUGHLY_TOKEN` user environment variable. Never commit a token.
- Every piece of work, including this instruction set, mockups and spikes, is a work item there. No work without an item.
- Statuses: `Open` (to do), `InProgress`, `Done`, `Cancelled`.
- Epics: Throughly has one level only (epic → item). Epics are long-lived areas, not features: `AWK-8` Foundation, `AWK-9` Brand & design system, `AWK-10` Product UX. Every item belongs to one epic. A new epic is created only for a new long-lived area, agreed with Dražan.
- Agent habits: `start_task` when picking an item up (assigns it, moves it to `InProgress`); a running timer (`start_timer` / `stop_timer` with a summary of what was done) while working; `link_commit` for every commit. Throughly's suggested branch name is ignored; branch names follow section 4.
- Discussion happens in Claude Code with Dražan. The outcome (decision, approved visual, links to artifacts) is written back into the work item's comments so Throughly stays the record.

## 2. Storing visuals and artifacts
- Rendered images (PNG, SVG, JPEG, WebP, GIF) are posted to the work item with the mockup upload (`add_task_mockup`), which shows them inline in a comment. One image per comment, captioned `W1 · <screen> · YYYY-MM-DD · source design/AWK-N-slug/<file>` (`W` = wireframe, `M` = hi-fi mockup).
- **Every visual is stored in the work item itself, as an image.** Links (e.g. a claude.ai artifact) may be added for convenience, never instead of the image. HTML mockups are rendered to PNG (Playwright) and uploaded; large boards are split into parts.
- Uploading from files: `bin/throughly-upload.py <manifest.json>` posts images from disk through the same Throughly MCP server (token from `THROUGHLY_TOKEN`), so large images need no copy-paste.
- PDFs and long documents: a Throughly wiki page linked to the work item, and a comment with the link. Throughly cannot attach HTML or PDF files to a work item directly.
- Comments cannot be edited. A revision is a new comment that says what it replaces: `W2 supersedes W1`.
- Sources (HTML, SVG, diagram files) are committed in `design/AWK-N-slug/` with a `design:` commit. Throughly holds the rendered images and links; the repo holds the sources.

## 3. The approval gate (mandatory)
Every feature, screen, data-model change and configuration surface follows this sequence:
1. Work item created or picked in Throughly, with scope stated in one paragraph.
2. Wireframe first: low-fidelity, grayscale, real copy in German and English where copy matters, states included (empty, loading, error, success). For data-model work the "wireframe" is the entity diagram plus the screens that read or write those entities. For Public API work it is the contract sketch (path, method, request, response, errors, example; see `public-api.md`), including the decision that the endpoint is public at all. For other work without a screen (documentation, infrastructure) it is an outline: a file tree, a document outline or a findings list.
3. Dražan reviews in Claude Code. He approves, changes, or rejects. The result is posted to the work item.
4. After wireframe approval, ask Dražan whether a high-fidelity mockup is wanted for this item. Only make one on his command. Hi-fi mockups use `ui-design-system.md` exactly.
5. Only after the final visual is approved does implementation start. Implementation must match the approved visual; deviations go back to step 3.

An agent that cannot find an approved visual for what it is about to build must stop and ask.

## 4. Branches, commits, pull requests
- Trunk-based: `main` is always deployable. Short-lived branches named `<type>/<work-item-key>-<slug>`, e.g. `feat/AWK-42-publish-to-domain`.
- A branch is deleted locally and on GitHub as soon as it is merged. Backups of history are kept as a bundle file outside the repository, never as branches.
- Every commit subject starts with `#<work item key>: `, followed by a Conventional Commit: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, `design:` (for wireframe and mockup sources). Example: `#AWK-42: feat(publishing): one-click domain publish`.
- The body says in a few short lines what was done and why, e.g. which behaviour a test pins down or what a change fixes.
- Because subjects start with `#`, git must not treat that line as a comment: set `git config core.commentChar ";"` in your clone (or commit with `--cleanup=whitespace`).
- Commits carry no `Signed-off-by` trailer: do not use `git commit -s` (Dražan, 2026-10-13).
- Commits and PRs carry no AI-tool attribution: no Co-Authored-By trailers for tools, no "generated with" footers, no session links. The human committer is the author.
- Maintainer work, while Askwerk is a one-person project (Dražan, 2026-10-16): one work item per local feature branch. Its atomic commits stay local and show red → green → refactor. Nothing is pushed until the feature is finished and the full run (`bin/test-all.sh --full`) is green. Then the branch is squashed locally into one commit on `main` (`git merge --squash`, then one commit in the format above), `main` is pushed, the commit is linked on the work item, and the branch is deleted. No pull request. A feature that runs longer than a day or two may push its branch to the private backup repository, never to the public one. If `main` moved meanwhile, update the branch from it and run the full run again before squashing.
- Outside contributors: one work item per PR. The PR description links the work item and the approved visual. PRs are squash-merged. Until the merge, the PR branch keeps its own commit history, which shows red → green → refactor; do not squash or rebase it away before review.
- CI must be green: build, tests, lint, type-check, axe on every app, Lighthouse on PRs that touch the runner (full list in `testing.md`, CI gates). CI steps are scripts in `/bin`, so any CI system can run them and a contributor can run them locally.

## 5. Definition of done
- Built test-first on every layer it touches, per `testing.md`: the tests that drove the implementation are in the PR, and the PR's commit list shows red → green → refactor.
- Security and performance checked explicitly: authorisation policy named for every new endpoint, tenant scoping verified by a test, performance budget measured for anything on the runner or results path, audit entries present for every data or settings change.
- Matches the approved visual, in both languages, in light and dark mode, on phone and desktop.
- Unit tests for logic, integration tests against PostgreSQL (Testcontainers) for persistence, Playwright for the user-facing flow.
- If the PR adds or changes a Public API endpoint (only when its work item decided it is public), it ships per `public-api.md`: endpoints, OpenAPI snapshot, authorisation and tenant-isolation tests, regenerated SDKs, `docs/api/` and a changelog entry. App API changes ship with the GUI change that needs them.
- Data-model changes have a migration, are reflected in `data-model.md` and the canonical diagram, and have an entry in `decisions.md`.
- Audit entries, structured logs and metrics for the new behaviour are in place on backend and frontend, per `observability.md`, Definition of done.
- Configuration added is configurable in the GUI, not only in a file.
- No new dependency without a `decisions.md` entry (see `coding-standards.md`, Dependencies).
- The Throughly work item has the final state, links, and screenshots of the real result.
- Public-facing changes update `docs/` (user docs), `docs/api/` (if the API changed) and `CHANGELOG.md`; a stranger reading the repo must not need this Throughly history to understand the feature.

## 6. Outside contributors
- Throughly is private to the maintainers. The public intake is GitHub: issues for bugs and proposals, pull requests for code.
- A maintainer mirrors accepted work into Throughly (`AWK`) and puts the key in the issue and the PR.
- An outside PR that adds or changes a screen, a data-model entity or a configuration surface posts its wireframe in the PR first; a maintainer approves it there before code review starts. Everything else in this file applies unchanged.
- `CONTRIBUTING.md` explains this and points contributors to `/agents` as the single rulebook.

## 7. Decision log
`decisions.md` is append-only: date, decision, reason, alternatives rejected, work item. A decision is changed by a new entry that names the entry it supersedes. Library choices always get an entry including the licence.
