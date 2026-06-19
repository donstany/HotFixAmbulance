# Pagination — Design

**Date:** 2026-06-19
**Status:** Approved (design); pending implementation plan
**Feature:** Server-side pagination for the triage error-groups table, end-to-end (backend + frontend).

## Problem

`TriageResult.Groups` is rendered whole: the API returns every error group, the
React table loads all of them, sorts client-side, and `MetricsPanel` aggregates
over the full array. A 1-hour window already yields ~39 groups; a 30-day window
yields a few hundred. There is no paging on either end. We want server-side
pagination with a numbered, good-UX table footer.

## Decisions (locked)

- **Server-side pagination.** The backend returns page slices + total counts;
  sorting moves server-side so it sorts the whole set, not just the visible page.
- **Page UX:** default **25** rows; selector for **10/25/50/100**; numbered page
  buttons with ellipsis + Prev/Next; "Showing X–Y of Z" caption.
- **Malformed params → 400; out-of-range page → graceful empty page.**
- **Scope:** error-groups table only. The `history` endpoint (no UI today) and
  persisting the summary are out of scope.

## Key constraint

The **CLI consumes `TriageResult.Groups` in-process** (`CliRenderer.RenderTableAsync`
iterates `result.Groups`; `TriageService.RunAsync` returns the full `TriageResult`).
Therefore `TriageResult` and `TriageService` **must not change shape**. Pagination
lives entirely in the HTTP layer and the frontend.

## Architecture

The analysis remains one-shot — grouping needs all logs at once. The backend keeps
computing and persisting the full group set per run (`TriageRun.ErrorGroupsJson`,
`TriageRun.GroupCount` already exist). Pagination is **server-side slicing over the
already-stored run**.

The HTTP layer splits the old single contract into two:

1. A lightweight **run header** (metadata + summary, no groups) returned by the
   run-creating/run-fetching endpoints.
2. A **paged groups** endpoint that slices the stored groups on demand.

## Backend contract

### Unchanged
- `TriageResult` (record with `Groups`) — the in-process domain/service result.
- `TriageService.RunAsync` — still returns the full `TriageResult`.
- CLI (`CliRenderer`, `Cli/Program.cs`) — unaffected; calls `RunAsync` directly.
- Persistence (`TriageRun`, `ITriageRunRepository`) — unchanged.

### New types (in `HotFixAmbulance.Api`)
- `PagedResult<T>` — `{ IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems, int TotalPages }`.
- `TriageSummary` — `{ int TotalGroups, int TotalOccurrences, int Fatal, int Error,
  int Warning, int WithSuggestions, int WithFixes }`.
  - Feeds `MetricsPanel` (`TotalOccurrences` = sum of `count`; `WithSuggestions`;
    `WithFixes`) and the header's group count. Severity counts enable a richer panel later.
- `TriageRunHeader` — all current `TriageResult` fields **minus `Groups`**, **plus
  `int TotalGroups` and `TriageSummary Summary`**:
  `{ Id, ApiName, RequestedAtUtc, Lookback, FromUtc, ToUtc, TotalLogs, IsTruncated,
  TotalGroups, Summary }`.

### Pure helper `GroupPager` (in `HotFixAmbulance.Api`, unit-tested in isolation)
- `TriageSummary Summarize(IReadOnlyList<ErrorGroup> all)`.
- `PagedResult<ErrorGroup> Paginate(IReadOnlyList<ErrorGroup> all, int page, int pageSize, GroupSort sort, SortDir dir)`.
- Sort comparator reproduces the current default: **severity rank desc**
  (Fatal > Error > Warning), with `count` desc as a stable tiebreaker. Supported
  sort keys: `severity`, `count`, `firstSeen`, `lastSeen`, `endpoint`,
  `exceptionType`, `correlations`. `dir` ∈ `asc`/`desc`.

### Endpoints
- `POST /api/triage/{apiName}` → runs the pipeline (full `TriageResult` internally),
  maps to **`TriageRunHeader`** (summary computed from `result.Groups`), returns it.
  No groups array in the response.
- `GET /api/triage/runs/{id}` → `TriageRunHeader` (Rehydrate deserializes groups → Summarize).
- `GET /api/triage/{apiName}/latest` → `TriageRunHeader`.
- **NEW** `GET /api/triage/runs/{id}/groups?page=1&pageSize=25&sort=severity&dir=desc`
  → `PagedResult<ErrorGroup>`. Loads run, deserializes groups, sorts, slices.
- `GET /api/triage/{apiName}/history` — unchanged (out of scope).

### Validation
- `page < 1`, `pageSize ∉ {10,25,50,100}`, or unknown `sort`/`dir` → **400 ProblemDetails**.
- `page > totalPages` (e.g. after a page-size change) → **200 with empty `items`** and
  correct `totalItems`/`totalPages`, so the UI recovers without an error.
- Run not found → **404** (as today).
- Defaults when params omitted: `page=1`, `pageSize=25`, `sort=severity`, `dir=desc`.

## Frontend

### `types.ts`
- Add `PagedResult<T>` and `TriageSummary` interfaces.
- Rename `TriageResult` → `TriageRunHeader` (drop `groups`, add `totalGroups: number`
  and `summary: TriageSummary`) to mirror the backend honestly.
- Add `GroupSort` / `SortDir` string unions and a `GroupsPageRequest`.

### `api.ts`
- `runTriage`, `fetchTriageById`, `fetchLatestTriage` → return `TriageRunHeader`.
- New `fetchGroupsPage(runId, { page, pageSize, sort, dir }, signal) → PagedResult<ErrorGroup>`.

### `App.tsx`
- `MetricsPanel` consumes `summary={r.summary}`.
- Header line "X log(s) in Y group(s)" uses `r.totalGroups`.
- `TriageTable` gets `runId={r.id}` (+ `analysisDateUtc`) instead of `groups`.

### `TriageTable.tsx`
- Internal state: `page`, `pageSize` (default 25), `sorting` (default severity desc).
- `useQuery` keyed `['groups', runId, page, pageSize, sortId, sortDir]` → `fetchGroupsPage`.
- `useReactTable` with `manualPagination: true`, `manualSorting: true`,
  `data = pagedResult.items`, `rowCount = pagedResult.totalItems`.
- `onSortingChange` updates sorting → resets to page 1 → refetch.
- `analysisIdentifier` column global index = `(page-1)*pageSize + visibleIndex + 1`.
- Per-page loading and empty states.
- Column-visibility settings + sticky header retained.

### New `Pagination.tsx`
- Props: `page, pageSize, totalItems, totalPages, onPageChange, onPageSizeChange,
  pageSizeOptions=[10,25,50,100]`.
- "Showing X–Y of Z" caption; rows-per-page `<select>`; numbered buttons with
  ellipsis (e.g. `1 … 4 5 [6] 7 8 … 20`), Prev/Next disabled at bounds.
- Accessible: `<nav aria-label="Pagination">`, `aria-current="page"` on the active page.

```
┌────────────────────────────────────────────────────────────┐
│  Showing 1–25 of 213            Rows per page: [ 25 ▾ ]      │
│  ‹ Prev    1  2  3  …  9    Next ›                           │
└────────────────────────────────────────────────────────────┘
```

## Testing (TDD)

### Backend unit (`GroupPager`)
- Slice math and `totalPages` (e.g. 213 items / 25 = 9 pages; page 9 has 13 items).
- Severity-desc default ordering with count tiebreaker.
- Sort by `count` asc.
- Empty list → `totalItems=0`, `totalPages=0`, empty items.
- Out-of-range page → empty items, correct totals.
- `Summarize` counts: occurrences, per-severity, with-suggestions, with-fixes.

### Backend integration
- Default page/size/sort → 25 items (or fewer), severity desc, correct metadata.
- `pageSize=10&page=2` → correct slice.
- Invalid `pageSize`/`page<1`/unknown `sort`/`dir` → 400.
- `page` beyond range → 200 empty items + correct totals.
- Run not found → 404.
- POST and `GET runs/{id}` return a header with `summary` + `totalGroups` and **no**
  `groups` array.
- Existing group-asserting tests (`POST_triage_runs_pipeline_and_persists`,
  `POST_triage_includes_explicit_where_to_fix_guidance_in_howtofix`) move to assert
  against the new groups endpoint.

### Frontend
- `Pagination.test.tsx`: caption text, Prev disabled on page 1, Next disabled on last,
  click page → `onPageChange`, change size → `onPageSizeChange`, ellipsis for many pages.
- `TriageTable.test.tsx`: rewrite against a mocked `fetchGroupsPage` — renders page
  items, clicking Next refetches with `page=2`, sort header toggles manual sort and
  resets to page 1.
- `MetricsPanel`: render from a `summary` object.
- `App.test.tsx`: update mocks to the header + paged-groups shapes.

## Migration / compatibility notes
- The HTTP contract change (POST/by-id/latest drop `groups`, gain `summary`/`totalGroups`)
  is internal to this repo's frontend; the CLI is unaffected.
- No DB schema change — `TriageRun.ErrorGroupsJson` and `GroupCount` already store what
  we need; the summary is computed on read.
- `Rehydrate` already deserializes all groups, so summarizing there is cheap.
