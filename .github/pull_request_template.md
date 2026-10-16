## Work item
AWK-… · issue #…

## What and why
<!-- One paragraph: what changes for users or integrators, and why. -->

## Approved visual
<!-- Link to the approved wireframe, entity diagram or API contract sketch (Throughly comment or issue/PR comment). "n/a" only for changes without a screen, entity, setting or endpoint. -->

## Test evidence (pure TDD)
<!-- Name the commits that show red → green → refactor, e.g. `test: …` (failing) → `feat: …` → `refactor: …`. -->

## Checklist (definition of done, agents/workflow.md)
- [ ] Every commit is signed off (`git commit -s`, DCO)
- [ ] Tests written first, on every layer this touches (`agents/testing.md`)
- [ ] Authorisation policy named for every new endpoint; tenant isolation tested
- [ ] Audit entries for every data or settings change
- [ ] Matches the approved visual in German and English, light and dark, phone and desktop
- [ ] Public API changes: OpenAPI snapshot, SDKs, `docs/api/` updated (`agents/public-api.md`)
- [ ] New dependency: entry in `agents/decisions.md` (licence, why we cannot write it, exit path)
- [ ] New setting is configurable in the GUI
- [ ] `CHANGELOG.md` and `docs/` updated for anything users or integrators notice
- [ ] Threat note below for anything touching auth, uploads, exports or domains

## Threat note
<!-- Only when relevant. -->
