---
name: tdd-cycle
description: The mandatory 6-step TDD cycle used by every HotFixAmbulance story. Invoke before touching code. Codifies how `test-author`, production work, and the pre-commit hook fit together.
---

# Skill: HotFixAmbulance TDD cycle

Every story — backend, frontend, or demo-api — follows these six steps. Do not skip.

## 1. Red (outer)

Write **one failing acceptance test** that pins the user-visible behaviour.

- Backend → integration test in `backend/tests/HotFixAmbulance.IntegrationTests` using `WebApplicationFactory`.
- Frontend → Playwright spec in `frontend/tests/e2e/*.spec.ts` (mock the API).

Delegate to the `test-author` subagent. Confirm the test fails.

## 2. Red (inner)

Write **one failing unit test** for the smallest unit you'll touch first.

- Backend → xUnit `[Fact]` in `backend/tests/HotFixAmbulance.UnitTests`.
- Frontend → Vitest + Testing-Library in the same folder as the component (`*.test.tsx`).

Delegate to `test-author`. Confirm it fails.

## 3. Green

Write the **minimum production code** that makes the inner test pass. No extras, no speculative APIs.

- Backend: `dotnet test --filter "FullyQualifiedName~<TestName>"` to verify.
- Frontend: `npm --prefix frontend test -- --run -t "<test name>"`.

## 4. Refactor

Rename, extract, deduplicate **while green**. Rerun the same focused command after every change. Stop when the design reads cleanly.

## 5. Local commit

Run the **full** suite via the pre-commit hook:

```powershell
./.claude/hooks/pre-commit.ps1
```

The hook runs `dotnet test`, `npm --prefix frontend test -- --run`, `dotnet format --verify-no-changes`, and `npm --prefix frontend run lint`. It exits non-zero on any failure and blocks `git commit`.

Then `git commit -m "<phase>.<step>: <imperative subject>"`. **No `--no-verify`. Ever.**

## 6. Document

Append a one-line row to `docs/dev-log.md`:

```
| <UTC date> | <phase.step> | <step 1..6> | <one-sentence outcome> |
```

The `post-tool-use.ps1` hook usually does this; double-check the row appeared.

## Anti-patterns

- "I'll write the test after the implementation." → No. Step 1 first.
- "Just this once with `--no-verify`." → No. Fix the failure or revert.
- "More than one test per cycle." → Split into two cycles.
- "Refactor on red." → Get green first; refactor only while green.
