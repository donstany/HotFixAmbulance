# Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add end-to-end server-side pagination for the triage error-groups table — backend slices/sorts a stored run; the React table renders one page at a time with a numbered footer.

**Architecture:** The analysis stays one-shot (full group set is computed and persisted per run). Pagination is server-side slicing over the stored run. The HTTP layer splits into a lightweight *run header* (metadata + summary, no groups) and a new *paged groups* endpoint. The in-process `TriageResult` and the CLI are untouched (the CLI consumes `Groups` directly).

**Tech Stack:** ASP.NET Core minimal API (.NET 10), xUnit + FluentAssertions + `WebApplicationFactory`; React 19 + TypeScript, `@tanstack/react-table` v8, `@tanstack/react-query` v5, Vitest + Testing Library.

**Spec:** [docs/superpowers/specs/2026-06-19-pagination-design.md](../specs/2026-06-19-pagination-design.md)

---

## Conventions for every task

- **TDD** (per the `tdd-cycle` skill): write the failing test, run it red, implement minimally, run it green, commit. No `--no-verify`.
- **Build lock (Windows):** a running backend (`HotFixAmbulance.Api`/`demo-api`) locks `bin` DLLs and breaks `dotnet test`. Before any backend `dotnet test`/commit, stop them:
  ```powershell
  Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
  ```
- **Commit gate:** `git commit` runs the pre-commit hook (`dotnet test`, `npm --prefix frontend test -- --run`, `dotnet format --verify-no-changes`, `npm --prefix frontend run lint`). Every commit must leave the **whole** repo green — that is why some tasks change production + tests together.
- **Commit subject style:** `Phase 13.<id>: <imperative subject>` (matches existing history).
- **Frontend `Severity`** serializes from the backend enum as the string `"Fatal"|"Error"|"Warning"` (the API registers `JsonStringEnumConverter`).

---

## File structure

**Backend (new):**
- `backend/src/HotFixAmbulance.Api/PagedResult.cs` — generic paging envelope.
- `backend/src/HotFixAmbulance.Api/TriageSummary.cs` — aggregate counts for the run header / metrics.
- `backend/src/HotFixAmbulance.Api/TriageRunHeader.cs` — HTTP run header (no groups).
- `backend/src/HotFixAmbulance.Api/GroupPager.cs` — pure summarize + paginate + sort + param parsing.
- `backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs` — unit tests for `GroupPager`.

**Backend (modified):**
- `backend/src/HotFixAmbulance.Api/Program.cs` — header mapping + new groups endpoint.
- `backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs` — header + groups-endpoint tests.

**Frontend (new):**
- `frontend/src/components/Pagination.tsx` + `Pagination.test.tsx` — presentational footer + `pageList` helper.
- `frontend/src/components/TriageGroupsPanel.tsx` + `TriageGroupsPanel.test.tsx` — data-fetching container.

**Frontend (modified):**
- `frontend/src/types.ts` — `PagedResult<T>`, `TriageSummary`, `TriageRunHeader` (rename), `GroupSort`/`SortDir`/`GroupsPageRequest`.
- `frontend/src/api.ts` — header return types + `fetchGroupsPage`.
- `frontend/src/components/MetricsPanel.tsx` (+ new `MetricsPanel.test.tsx`) — consume `summary`.
- `frontend/src/components/TriageTable.tsx` (+ `TriageTable.test.tsx`) — controlled manual pagination/sorting.
- `frontend/src/App.tsx` (+ `App.test.tsx`) — wire header + `TriageGroupsPanel`.

---

## Task A1: Paging envelope, summary type, and `GroupPager.Summarize`

**Files:**
- Create: `backend/src/HotFixAmbulance.Api/PagedResult.cs`
- Create: `backend/src/HotFixAmbulance.Api/TriageSummary.cs`
- Create: `backend/src/HotFixAmbulance.Api/GroupPager.cs`
- Test: `backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs`:

```csharp
using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class GroupPagerTests
{
    private static ErrorGroup Group(Severity sev, int count, string? suggestion = null, string? howToFix = null) => new()
    {
        Severity = sev,
        Count = count,
        FirstSeenUtc = new DateTimeOffset(2026, 6, 19, 8, 0, 0, TimeSpan.Zero),
        LastSeenUtc = new DateTimeOffset(2026, 6, 19, 9, 0, 0, TimeSpan.Zero),
        ExceptionType = null,
        Message = null,
        Endpoint = null,
        HttpStatus = null,
        ServiceVersion = null,
        CorrelationIdCount = 0,
        Suggestion = suggestion,
        HowToFix = howToFix,
    };

    [Fact]
    public void Summarize_counts_occurrences_severities_and_ai_columns()
    {
        var groups = new[]
        {
            Group(Severity.Fatal, 3, suggestion: "s"),
            Group(Severity.Error, 5, howToFix: "fix"),
            Group(Severity.Warning, 2, suggestion: "s", howToFix: "fix"),
        };

        var summary = GroupPager.Summarize(groups);

        summary.TotalGroups.Should().Be(3);
        summary.TotalOccurrences.Should().Be(10);
        summary.Fatal.Should().Be(1);
        summary.Error.Should().Be(1);
        summary.Warning.Should().Be(1);
        summary.WithSuggestions.Should().Be(2);
        summary.WithFixes.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test backend/tests/HotFixAmbulance.UnitTests --filter "FullyQualifiedName~GroupPagerTests"
```
Expected: FAIL — `PagedResult`/`TriageSummary`/`GroupPager` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/HotFixAmbulance.Api/PagedResult.cs`:

```csharp
namespace HotFixAmbulance.Api;

/// <summary>
/// Transport envelope for a single page of results plus the totals a UI needs to render
/// pagination controls. Serialized to the React app.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
```

Create `backend/src/HotFixAmbulance.Api/TriageSummary.cs`:

```csharp
namespace HotFixAmbulance.Api;

/// <summary>
/// Whole-run aggregates computed across ALL error groups. Lets the UI show metrics and a
/// total group count without downloading every group (which is now paginated).
/// </summary>
public sealed record TriageSummary(
    int TotalGroups,
    int TotalOccurrences,
    int Fatal,
    int Error,
    int Warning,
    int WithSuggestions,
    int WithFixes);
```

Create `backend/src/HotFixAmbulance.Api/GroupPager.cs`:

```csharp
using HotFixAmbulance.Core;

namespace HotFixAmbulance.Api;

/// <summary>Sort keys the paged groups endpoint accepts.</summary>
public enum GroupSortKey { Severity, Count, FirstSeen, LastSeen, Endpoint, ExceptionType, Correlations }

public enum SortDirection { Asc, Desc }

/// <summary>
/// Pure (no I/O) helpers that summarize, sort, and slice a fully-materialized list of
/// <see cref="ErrorGroup"/>. Used by the HTTP layer; unit-tested in isolation.
/// </summary>
public static class GroupPager
{
    public static readonly IReadOnlyList<int> AllowedPageSizes = new[] { 10, 25, 50, 100 };

    public static TriageSummary Summarize(IReadOnlyList<ErrorGroup> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return new TriageSummary(
            TotalGroups: all.Count,
            TotalOccurrences: all.Sum(g => g.Count),
            Fatal: all.Count(g => g.Severity == Severity.Fatal),
            Error: all.Count(g => g.Severity == Severity.Error),
            Warning: all.Count(g => g.Severity == Severity.Warning),
            WithSuggestions: all.Count(g => !string.IsNullOrWhiteSpace(g.Suggestion)),
            WithFixes: all.Count(g => !string.IsNullOrWhiteSpace(g.HowToFix)));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test backend/tests/HotFixAmbulance.UnitTests --filter "FullyQualifiedName~GroupPagerTests"
```
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HotFixAmbulance.Api/PagedResult.cs backend/src/HotFixAmbulance.Api/TriageSummary.cs backend/src/HotFixAmbulance.Api/GroupPager.cs backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs
git commit -m "Phase 13.A1: PagedResult + TriageSummary + GroupPager.Summarize"
```

---

## Task A2: `GroupPager.Paginate` (sort + slice) and param parsing

**Files:**
- Modify: `backend/src/HotFixAmbulance.Api/GroupPager.cs`
- Test: `backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these methods inside the `GroupPagerTests` class:

```csharp
    [Fact]
    public void Paginate_default_severity_desc_then_count_desc()
    {
        var groups = new[]
        {
            Group(Severity.Warning, 9),
            Group(Severity.Fatal, 1),
            Group(Severity.Error, 2),
            Group(Severity.Error, 8),
        };

        var page = GroupPager.Paginate(groups, page: 1, pageSize: 10, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Select(g => (g.Severity, g.Count)).Should().ContainInOrder(
            (Severity.Fatal, 1),
            (Severity.Error, 8),
            (Severity.Error, 2),
            (Severity.Warning, 9));
    }

    [Fact]
    public void Paginate_slices_and_reports_totals()
    {
        var groups = Enumerable.Range(0, 23).Select(i => Group(Severity.Error, i)).ToArray();

        var page2 = GroupPager.Paginate(groups, page: 2, pageSize: 10, GroupSortKey.Count, SortDirection.Asc);

        page2.Page.Should().Be(2);
        page2.PageSize.Should().Be(10);
        page2.TotalItems.Should().Be(23);
        page2.TotalPages.Should().Be(3);
        page2.Items.Should().HaveCount(10);
        page2.Items.Select(g => g.Count).Should().ContainInOrder(10, 11, 12, 13, 14, 15, 16, 17, 18, 19);
    }

    [Fact]
    public void Paginate_out_of_range_page_returns_empty_items_with_correct_totals()
    {
        var groups = Enumerable.Range(0, 5).Select(i => Group(Severity.Error, i)).ToArray();

        var page = GroupPager.Paginate(groups, page: 9, pageSize: 10, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(5);
        page.TotalPages.Should().Be(1);
    }

    [Fact]
    public void Paginate_empty_list_has_zero_pages()
    {
        var page = GroupPager.Paginate(Array.Empty<ErrorGroup>(), page: 1, pageSize: 25, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(0);
        page.TotalPages.Should().Be(0);
    }

    [Theory]
    [InlineData("severity", true, GroupSortKey.Severity)]
    [InlineData("COUNT", true, GroupSortKey.Count)]
    [InlineData("firstSeen", true, GroupSortKey.FirstSeen)]
    [InlineData(null, true, GroupSortKey.Severity)]
    [InlineData("bogus", false, GroupSortKey.Severity)]
    public void TryParseSort_handles_known_unknown_and_default(string? value, bool ok, GroupSortKey expected)
    {
        GroupPager.TryParseSort(value, out var key).Should().Be(ok);
        if (ok) key.Should().Be(expected);
    }

    [Theory]
    [InlineData("asc", true, SortDirection.Asc)]
    [InlineData("DESC", true, SortDirection.Desc)]
    [InlineData(null, true, SortDirection.Desc)]
    [InlineData("sideways", false, SortDirection.Desc)]
    public void TryParseDir_handles_known_unknown_and_default(string? value, bool ok, SortDirection expected)
    {
        GroupPager.TryParseDir(value, out var dir).Should().Be(ok);
        if (ok) dir.Should().Be(expected);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test backend/tests/HotFixAmbulance.UnitTests --filter "FullyQualifiedName~GroupPagerTests"
```
Expected: FAIL — `Paginate`/`TryParseSort`/`TryParseDir` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add these members to `GroupPager` in `backend/src/HotFixAmbulance.Api/GroupPager.cs`:

```csharp
    public static PagedResult<ErrorGroup> Paginate(
        IReadOnlyList<ErrorGroup> all, int page, int pageSize, GroupSortKey sort, SortDirection dir)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (!AllowedPageSizes.Contains(pageSize)) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var sorted = Sort(all, sort, dir);
        var totalItems = sorted.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<ErrorGroup>(items, page, pageSize, totalItems, totalPages);
    }

    public static bool TryParseSort(string? value, out GroupSortKey key)
    {
        key = GroupSortKey.Severity;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "severity": key = GroupSortKey.Severity; return true;
            case "count": key = GroupSortKey.Count; return true;
            case "firstseen": key = GroupSortKey.FirstSeen; return true;
            case "lastseen": key = GroupSortKey.LastSeen; return true;
            case "endpoint": key = GroupSortKey.Endpoint; return true;
            case "exceptiontype": key = GroupSortKey.ExceptionType; return true;
            case "correlations": key = GroupSortKey.Correlations; return true;
            default: return false;
        }
    }

    public static bool TryParseDir(string? value, out SortDirection dir)
    {
        dir = SortDirection.Desc;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "desc": dir = SortDirection.Desc; return true;
            case "asc": dir = SortDirection.Asc; return true;
            default: return false;
        }
    }

    private static IReadOnlyList<ErrorGroup> Sort(IReadOnlyList<ErrorGroup> all, GroupSortKey key, SortDirection dir)
    {
        var primary = key switch
        {
            GroupSortKey.Count => Order(all, g => (IComparable)g.Count, dir),
            GroupSortKey.FirstSeen => Order(all, g => g.FirstSeenUtc, dir),
            GroupSortKey.LastSeen => Order(all, g => g.LastSeenUtc, dir),
            GroupSortKey.Endpoint => Order(all, g => g.Endpoint ?? string.Empty, dir),
            GroupSortKey.ExceptionType => Order(all, g => g.ExceptionType ?? string.Empty, dir),
            GroupSortKey.Correlations => Order(all, g => g.CorrelationIdCount, dir),
            _ => Order(all, g => g.Severity, dir),
        };
        // Stable tiebreaker mirrors ErrorGroup.RankBySeverity: count desc, then last-seen desc.
        return primary.ThenByDescending(g => g.Count).ThenByDescending(g => g.LastSeenUtc).ToList();
    }

    private static IOrderedEnumerable<ErrorGroup> Order<TKey>(
        IReadOnlyList<ErrorGroup> all, Func<ErrorGroup, TKey> selector, SortDirection dir)
        => dir == SortDirection.Desc ? all.OrderByDescending(selector) : all.OrderBy(selector);
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test backend/tests/HotFixAmbulance.UnitTests --filter "FullyQualifiedName~GroupPagerTests"
```
Expected: PASS (all GroupPager tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HotFixAmbulance.Api/GroupPager.cs backend/tests/HotFixAmbulance.UnitTests/Api/GroupPagerTests.cs
git commit -m "Phase 13.A2: GroupPager.Paginate + sort + param parsing"
```

---

## Task A3: New `GET /api/triage/runs/{id}/groups` endpoint

**Files:**
- Modify: `backend/src/HotFixAmbulance.Api/Program.cs` (add endpoint after the `runs/{id}` GET, ~line 134)
- Test: `backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs`

This task is **additive** — existing endpoints keep returning the full `TriageResult`, so all current tests stay green.

- [ ] **Step 1: Write the failing tests**

Append to the `TriageEndpointsTests` class (before the `private sealed record TriagePayload` line). Note the test fixture seeds exactly one error log, so a run has exactly one group.

```csharp
    [Fact]
    public async Task GET_groups_returns_first_page_with_metadata()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups", UriKind.Relative));

        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(25);
        root.GetProperty("totalItems").GetInt32().Should().Be(1);
        root.GetProperty("totalPages").GetInt32().Should().Be(1);
        root.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GET_groups_with_invalid_pageSize_returns_400()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?pageSize=7", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_groups_with_unknown_sort_returns_400()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?sort=bogus", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_groups_out_of_range_page_returns_empty_items()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?page=5", UriKind.Relative));

        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("totalItems").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GET_groups_for_unknown_run_returns_404()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync(new Uri($"/api/triage/runs/{Guid.NewGuid()}/groups", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test backend/tests/HotFixAmbulance.IntegrationTests --filter "FullyQualifiedName~GET_groups"
```
Expected: FAIL — endpoint returns 404 for all (route not mapped).

- [ ] **Step 3: Write minimal implementation**

In `backend/src/HotFixAmbulance.Api/Program.cs`, add this endpoint immediately after the `GET /api/triage/runs/{id:guid}` block (after line 134):

```csharp
app.MapGet("/api/triage/runs/{id:guid}/groups", async (
    Guid id,
    [FromQuery] int? page,
    [FromQuery] int? pageSize,
    [FromQuery] string? sort,
    [FromQuery] string? dir,
    ITriageRunRepository repo,
    CancellationToken ct) =>
{
    var p = page ?? 1;
    var ps = pageSize ?? 25;

    if (p < 1)
    {
        return Results.Problem(detail: "page must be >= 1.", statusCode: StatusCodes.Status400BadRequest);
    }
    if (!GroupPager.AllowedPageSizes.Contains(ps))
    {
        return Results.Problem(
            detail: $"pageSize must be one of {string.Join(", ", GroupPager.AllowedPageSizes)}.",
            statusCode: StatusCodes.Status400BadRequest);
    }
    if (!GroupPager.TryParseSort(sort, out var sortKey))
    {
        return Results.Problem(detail: "Unknown sort key.", statusCode: StatusCodes.Status400BadRequest);
    }
    if (!GroupPager.TryParseDir(dir, out var dirVal))
    {
        return Results.Problem(detail: "dir must be 'asc' or 'desc'.", statusCode: StatusCodes.Status400BadRequest);
    }

    var run = await repo.GetByIdAsync(id, ct);
    if (run is null) return Results.NotFound();

    var all = JsonSerializer.Deserialize<List<ErrorGroup>>(run.ErrorGroupsJson) ?? new List<ErrorGroup>();
    var paged = GroupPager.Paginate(all, p, ps, sortKey, dirVal);
    return Results.Ok(paged);
});
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test backend/tests/HotFixAmbulance.IntegrationTests --filter "FullyQualifiedName~GET_groups"
```
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HotFixAmbulance.Api/Program.cs backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs
git commit -m "Phase 13.A3: paged groups endpoint (GET /runs/{id}/groups)"
```

---

## Task A4: Run-creating/run-fetching endpoints return `TriageRunHeader`

**Files:**
- Create: `backend/src/HotFixAmbulance.Api/TriageRunHeader.cs`
- Modify: `backend/src/HotFixAmbulance.Api/Program.cs` (POST + `latest` + `runs/{id}` + add `ToHeader`)
- Modify: `backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs` (update group-asserting tests + `TriagePayload`)

This commit changes the HTTP contract and the tests that asserted on it **together**, so the suite stays green.

- [ ] **Step 1: Update the existing tests to the new contract (these are the failing tests)**

In `backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs`:

a) Replace the body of `POST_triage_runs_pipeline_and_persists` with:

```csharp
    [Fact]
    public async Task POST_triage_runs_pipeline_and_persists()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TriageHeaderPayload>();
        payload.Should().NotBeNull();
        payload!.ApiName.Should().Be("checkout-api");
        payload.TotalLogs.Should().Be(1);
        payload.TotalGroups.Should().Be(1);
        payload.Summary.TotalOccurrences.Should().Be(1);

        // The header carries no inline groups; they come from the paged endpoint.
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("groups", out _).Should().BeFalse();

        var groups = await client.GetAsync(new Uri($"/api/triage/runs/{payload.Id}/groups", UriKind.Relative));
        groups.EnsureSuccessStatusCode();
        JsonDocument.Parse(await groups.Content.ReadAsStringAsync())
            .RootElement.GetProperty("items").GetArrayLength().Should().Be(1);

        var latest = await client.GetAsync(new Uri("/api/triage/checkout-api/latest", UriKind.Relative));
        latest.StatusCode.Should().Be(HttpStatusCode.OK);
    }
```

b) Replace the body of `POST_triage_includes_explicit_where_to_fix_guidance_in_howtofix` with:

```csharp
    [Fact]
    public async Task POST_triage_includes_explicit_where_to_fix_guidance_in_howtofix()
    {
        using var client = _factory.CreateClient();

        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);

        var howToFix = items[0].GetProperty("howToFix").GetString();
        howToFix.Should().NotBeNullOrWhiteSpace();
        howToFix!.Should().Contain("Where to fix", because: "recommendations must explicitly tell developers where to apply the change");
    }
```

c) Replace the `TriagePayload` record line:

```csharp
    private sealed record TriageHeaderPayload(Guid Id, string ApiName, int TotalLogs, int TotalGroups, SummaryPayload Summary);
    private sealed record SummaryPayload(int TotalGroups, int TotalOccurrences);
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test backend/tests/HotFixAmbulance.IntegrationTests --filter "FullyQualifiedName~POST_triage_runs_pipeline_and_persists|FullyQualifiedName~POST_triage_includes_explicit_where_to_fix"
```
Expected: FAIL — POST still returns a `groups` array and has no `totalGroups`/`summary`.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/HotFixAmbulance.Api/TriageRunHeader.cs`:

```csharp
namespace HotFixAmbulance.Api;

/// <summary>
/// HTTP shape for a triage run WITHOUT its error groups. Returned by run-creating and
/// run-fetching endpoints; the groups are fetched separately and paginated via
/// <c>GET /api/triage/runs/{id}/groups</c>.
/// </summary>
public sealed record TriageRunHeader(
    Guid Id,
    string ApiName,
    DateTimeOffset RequestedAtUtc,
    TimeSpan Lookback,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalLogs,
    bool IsTruncated,
    int TotalGroups,
    TriageSummary Summary);
```

In `backend/src/HotFixAmbulance.Api/Program.cs`:

- POST handler (line 114–115): replace `return Results.Ok(result);` with:
  ```csharp
  return Results.Ok(ToHeader(result));
  ```
- `latest` handler (line 124): replace `Results.Ok(Rehydrate(run))` with:
  ```csharp
  return run is null ? Results.NotFound() : Results.Ok(ToHeader(Rehydrate(run)));
  ```
- `runs/{id}` handler (line 133): replace `Results.Ok(Rehydrate(run))` with:
  ```csharp
  return run is null ? Results.NotFound() : Results.Ok(ToHeader(Rehydrate(run)));
  ```
- Add a mapper next to the existing `Rehydrate` static method (after line 164):
  ```csharp
  static TriageRunHeader ToHeader(TriageResult r)
  {
      var summary = GroupPager.Summarize(r.Groups);
      return new TriageRunHeader(
          r.Id, r.ApiName, r.RequestedAtUtc, r.Lookback,
          r.FromUtc, r.ToUtc, r.TotalLogs, r.IsTruncated,
          summary.TotalGroups, summary);
  }
  ```

- [ ] **Step 4: Run the full backend suite to verify green**

```powershell
dotnet test backend/tests/HotFixAmbulance.IntegrationTests
dotnet test backend/tests/HotFixAmbulance.UnitTests
```
Expected: PASS (all). The absolute-window/round-trip/default-lookback tests still pass because the header keeps `fromUtc`/`toUtc`/`lookback`/`isTruncated`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HotFixAmbulance.Api/TriageRunHeader.cs backend/src/HotFixAmbulance.Api/Program.cs backend/tests/HotFixAmbulance.IntegrationTests/Api/TriageEndpointsTests.cs
git commit -m "Phase 13.A4: run endpoints return TriageRunHeader (summary, no inline groups)"
```

---

## Task B1: `Pagination` presentational component

**Files:**
- Create: `frontend/src/components/Pagination.tsx`
- Test: `frontend/src/components/Pagination.test.tsx`

Additive — nothing imports it yet, so the build stays green.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/Pagination.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Pagination, pageList } from './Pagination';

describe('pageList', () => {
  it('lists every page when there are few', () => {
    expect(pageList(1, 3)).toEqual([1, 2, 3]);
  });
  it('inserts ellipses around the current page in long ranges', () => {
    expect(pageList(6, 20)).toEqual([1, 'ellipsis', 5, 6, 7, 'ellipsis', 20]);
  });
});

describe('<Pagination />', () => {
  const base = {
    page: 1,
    pageSize: 25,
    totalItems: 213,
    totalPages: 9,
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
  };

  it('shows the range caption', () => {
    render(<Pagination {...base} />);
    expect(screen.getByText(/Showing 1.*25 of 213/)).toBeInTheDocument();
  });

  it('disables Prev on the first page', () => {
    render(<Pagination {...base} />);
    expect(screen.getByRole('button', { name: /prev/i })).toBeDisabled();
  });

  it('disables Next on the last page', () => {
    render(<Pagination {...base} page={9} />);
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });

  it('emits the target page when a number is clicked', async () => {
    const onPageChange = vi.fn();
    render(<Pagination {...base} onPageChange={onPageChange} />);
    await userEvent.click(screen.getByRole('button', { name: '3' }));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  it('emits a new page size when the selector changes', async () => {
    const onPageSizeChange = vi.fn();
    render(<Pagination {...base} onPageSizeChange={onPageSizeChange} />);
    await userEvent.selectOptions(screen.getByLabelText(/rows per page/i), '50');
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
npm --prefix frontend test -- --run src/components/Pagination.test.tsx
```
Expected: FAIL — module `./Pagination` not found.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/Pagination.tsx`:

```tsx
import { ChevronLeft, ChevronRight } from 'lucide-react';

/** Build a compact page list with ellipses: e.g. pageList(6, 20) → [1,'ellipsis',5,6,7,'ellipsis',20]. */
export function pageList(current: number, total: number): (number | 'ellipsis')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | 'ellipsis')[] = [1];
  const start = Math.max(2, current - 1);
  const end = Math.min(total - 1, current + 1);
  if (start > 2) pages.push('ellipsis');
  for (let p = start; p <= end; p++) pages.push(p);
  if (end < total - 1) pages.push('ellipsis');
  pages.push(total);
  return pages;
}

interface PaginationProps {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  pageSizeOptions?: number[];
}

export function Pagination({
  page,
  pageSize,
  totalItems,
  totalPages,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 25, 50, 100],
}: PaginationProps) {
  const from = totalItems === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalItems);

  return (
    <nav
      aria-label="Pagination"
      className="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-slate-600"
    >
      <div className="flex items-center gap-3">
        <span>
          Showing <span className="font-medium text-slate-800">{from}</span>–
          <span className="font-medium text-slate-800">{to}</span> of{' '}
          <span className="font-medium text-slate-800">{totalItems}</span>
        </span>
        <label className="flex items-center gap-1.5">
          <span className="text-slate-500">Rows per page</span>
          <select
            aria-label="Rows per page"
            value={pageSize}
            onChange={e => onPageSizeChange(Number(e.target.value))}
            className="rounded border border-slate-300 px-2 py-1 text-sm"
          >
            {pageSizeOptions.map(o => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          className="inline-flex items-center gap-1 rounded-md border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <ChevronLeft size={14} aria-hidden="true" />
          Prev
        </button>

        {pageList(page, Math.max(totalPages, 1)).map((p, i) =>
          p === 'ellipsis' ? (
            <span key={`e${i}`} className="px-2 text-slate-400" aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              onClick={() => onPageChange(p)}
              aria-current={p === page ? 'page' : undefined}
              className={`min-w-[2rem] rounded-md px-2 py-1 text-xs font-medium transition ${
                p === page
                  ? 'bg-slate-900 text-white shadow-sm'
                  : 'border border-slate-300 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              {p}
            </button>
          ),
        )}

        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages}
          className="inline-flex items-center gap-1 rounded-md border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Next
          <ChevronRight size={14} aria-hidden="true" />
        </button>
      </div>
    </nav>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm --prefix frontend test -- --run src/components/Pagination.test.tsx
```
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/Pagination.tsx frontend/src/components/Pagination.test.tsx
git commit -m "Phase 13.B1: Pagination component + pageList helper"
```

---

## Task B2: Frontend contract migration (types, api, MetricsPanel, TriageTable, container, App)

This is one commit because the type rename interlocks across these files — TypeScript will not compile until they all move together. Work through the sub-steps, then run the whole frontend suite once before committing.

**Files:**
- Modify: `frontend/src/types.ts`
- Modify: `frontend/src/api.ts`
- Modify: `frontend/src/components/MetricsPanel.tsx`; Create: `frontend/src/components/MetricsPanel.test.tsx`
- Modify: `frontend/src/components/TriageTable.tsx`; Modify: `frontend/src/components/TriageTable.test.tsx`
- Create: `frontend/src/components/TriageGroupsPanel.tsx`; Create: `frontend/src/components/TriageGroupsPanel.test.tsx`
- Modify: `frontend/src/App.tsx`; Modify: `frontend/src/App.test.tsx`

- [ ] **Step 1: `types.ts` — new transport types**

Replace the `TriageResult` interface (lines 25–39) and the section after it with:

```typescript
/** Mirrors backend `HotFixAmbulance.Api.PagedResult<T>`. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

/** Mirrors backend `HotFixAmbulance.Api.TriageSummary`. */
export interface TriageSummary {
  totalGroups: number;
  totalOccurrences: number;
  fatal: number;
  error: number;
  warning: number;
  withSuggestions: number;
  withFixes: number;
}

/** Mirrors backend `HotFixAmbulance.Api.TriageRunHeader` (a run WITHOUT its groups). */
export interface TriageRunHeader {
  id: string;
  apiName: string;
  requestedAtUtc: string;
  lookback: string;
  /** Inclusive start of the analysis window (UTC ISO-8601). */
  fromUtc: string;
  /** Inclusive end of the analysis window (UTC ISO-8601). */
  toUtc: string;
  /** True when Elastic returned the MaxDocuments cap (results may be incomplete). */
  isTruncated: boolean;
  totalLogs: number;
  totalGroups: number;
  summary: TriageSummary;
}

/** Sort keys accepted by GET /api/triage/runs/{id}/groups. */
export type GroupSort =
  | 'severity'
  | 'count'
  | 'firstSeen'
  | 'lastSeen'
  | 'endpoint'
  | 'exceptionType'
  | 'correlations';

export type SortDir = 'asc' | 'desc';

export interface GroupsPageRequest {
  page: number;
  pageSize: number;
  sort: GroupSort;
  dir: SortDir;
}
```

Keep `ErrorGroup` and `TimeRangeSelection` exactly as they are.

- [ ] **Step 2: `api.ts` — header return types + `fetchGroupsPage`**

Replace the import on line 1 and the three triage functions:

```typescript
import type {
  ErrorGroup,
  GroupsPageRequest,
  PagedResult,
  TimeRangeSelection,
  TriageRunHeader,
} from './types';
```

```typescript
export async function fetchTriageById(id: string, signal?: AbortSignal): Promise<TriageRunHeader> {
  const res = await fetch(`${baseUrl()}/api/triage/runs/${encodeURIComponent(id)}`, { signal });
  if (!res.ok) {
    throw new Error(`GET /api/triage/runs/${id} failed: ${res.status}`);
  }
  return (await res.json()) as TriageRunHeader;
}

export async function fetchLatestTriage(apiName: string, signal?: AbortSignal): Promise<TriageRunHeader> {
  const res = await fetch(`${baseUrl()}/api/triage/${encodeURIComponent(apiName)}/latest`, { signal });
  if (!res.ok) {
    throw new Error(`GET /api/triage/${apiName}/latest failed: ${res.status}`);
  }
  return (await res.json()) as TriageRunHeader;
}
```

Change `runTriage`'s return type from `Promise<TriageResult>` to `Promise<TriageRunHeader>` and its final cast to `as TriageRunHeader` (body otherwise unchanged). Then append:

```typescript
/** Fetches one sorted page of a run's error groups. */
export async function fetchGroupsPage(
  runId: string,
  req: GroupsPageRequest,
  signal?: AbortSignal,
): Promise<PagedResult<ErrorGroup>> {
  const params = new URLSearchParams({
    page: String(req.page),
    pageSize: String(req.pageSize),
    sort: req.sort,
    dir: req.dir,
  });
  const res = await fetch(
    `${baseUrl()}/api/triage/runs/${encodeURIComponent(runId)}/groups?${params.toString()}`,
    { signal },
  );
  if (!res.ok) {
    throw new Error(`GET /api/triage/runs/${runId}/groups failed: ${res.status}`);
  }
  return (await res.json()) as PagedResult<ErrorGroup>;
}
```

- [ ] **Step 3: `MetricsPanel` — consume `summary` (test first)**

Create `frontend/src/components/MetricsPanel.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MetricsPanel } from './MetricsPanel';
import type { TriageSummary } from '../types';

const SUMMARY: TriageSummary = {
  totalGroups: 7,
  totalOccurrences: 1234,
  fatal: 1,
  error: 4,
  warning: 2,
  withSuggestions: 5,
  withFixes: 3,
};

describe('<MetricsPanel />', () => {
  it('shows total occurrences, AI insights and fix counts from the summary', () => {
    render(<MetricsPanel summary={SUMMARY} />);
    expect(screen.getByText('1,234')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });
});
```

Then change `MetricsPanel.tsx` (lines 1–12):

```tsx
import { AlertTriangle, Lightbulb, Wrench } from 'lucide-react';
import type { TriageSummary } from '../types';

interface MetricsPanelProps {
  summary: TriageSummary;
}

export function MetricsPanel({ summary }: MetricsPanelProps) {
  const totalErrors = summary.totalOccurrences;
  const withSuggestions = summary.withSuggestions;
  const withFixes = summary.withFixes;
```

(The `metrics` array and JSX below stay unchanged — they already read `totalErrors`, `withSuggestions`, `withFixes`.)

- [ ] **Step 4: `TriageTable` — controlled manual pagination + sorting (update tests)**

Replace `frontend/src/components/TriageTable.test.tsx` entirely:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import type { SortingState } from '@tanstack/react-table';
import { TriageTable } from './TriageTable';
import type { ErrorGroup } from '../types';

const SAMPLE: ErrorGroup[] = [
  {
    severity: 'Fatal',
    count: 1,
    firstSeenUtc: '2026-06-16T11:00:00Z',
    lastSeenUtc: '2026-06-16T11:00:00Z',
    exceptionType: 'OutOfMemoryException',
    message: 'Heap exhausted',
    endpoint: '/checkout',
    httpStatus: 500,
    serviceVersion: '1.1.0',
    correlationIdCount: 1,
    stackFile: null,
    stackSymbol: null,
    stackLine: null,
    suggestion: 'OOM under load',
    howToFix: 'beef456 (2026-06-14) — tune GC settings',
  },
  {
    severity: 'Warning',
    count: 4,
    firstSeenUtc: '2026-06-16T10:00:00Z',
    lastSeenUtc: '2026-06-16T10:05:00Z',
    exceptionType: 'TimeoutException',
    message: 'Operation timed out',
    endpoint: '/orders',
    httpStatus: 504,
    serviceVersion: '1.1.0',
    correlationIdCount: 2,
    stackFile: 'PaymentGateway.cs',
    stackSymbol: 'PaymentGateway.AuthorizeAsync',
    stackLine: 88,
    suggestion: 'Upstream timeout',
    howToFix: 'abcd123 (2026-06-15) — bump HttpClient timeout',
  },
];

function renderTable(overrides: Partial<React.ComponentProps<typeof TriageTable>> = {}) {
  const props: React.ComponentProps<typeof TriageTable> = {
    groups: SAMPLE,
    analysisDateUtc: '2026-06-18T09:30:00Z',
    page: 1,
    pageSize: 25,
    totalItems: SAMPLE.length,
    totalPages: 1,
    sorting: [{ id: 'severity', desc: true }] as SortingState,
    onSortingChange: vi.fn(),
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
    ...overrides,
  };
  return render(<TriageTable {...props} />);
}

describe('<TriageTable />', () => {
  it('renders rows in the order given (server already sorted them)', () => {
    renderTable();
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]).getByTestId('severity-badge')).toHaveTextContent('Fatal');
  });

  it('numbers the ID column using the page offset', () => {
    renderTable({ page: 2, pageSize: 25 });
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]).getAllByRole('cell')[0]).toHaveTextContent('26-18062026');
  });

  it('calls onSortingChange when a sortable header is clicked', async () => {
    const onSortingChange = vi.fn();
    const { default: userEvent } = await import('@testing-library/user-event');
    const user = userEvent.setup();
    renderTable({ onSortingChange });
    await user.click(screen.getByRole('columnheader', { name: /count/i }));
    expect(onSortingChange).toHaveBeenCalled();
  });

  it('shows the suggestion and howToFix AI columns with distinct text', () => {
    renderTable();
    const suggestions = screen.getAllByTestId('suggestion').map(el => el.textContent);
    const fixes = screen.getAllByTestId('howtofix').map(el => el.textContent);
    suggestions.forEach((s, i) => expect(s).not.toEqual(fixes[i]));
  });

  it('renders the stack frame under the exception when present', () => {
    renderTable();
    const frame = screen.getByTestId('stackframe');
    expect(frame.textContent).toMatch(/PaymentGateway\.cs:88/);
  });

  it('renders the pagination footer', () => {
    renderTable({ totalItems: 60, totalPages: 3 });
    expect(screen.getByRole('navigation', { name: /pagination/i })).toBeInTheDocument();
  });
});
```

Then edit `frontend/src/components/TriageTable.tsx`:

1) Imports — replace the `@tanstack/react-table` import block and add `OnChangeFn`, drop `getSortedRowModel`, and import `Pagination`:

```tsx
import { useMemo, useState, useEffect } from 'react';
import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  useReactTable,
  type OnChangeFn,
  type SortingState,
} from '@tanstack/react-table';
```

Add with the other local imports:

```tsx
import { Pagination } from './Pagination';
```

2) Signature — replace the component signature (line 114) and remove the internal `sorting` state (lines 115). New signature:

```tsx
export function TriageTable({
  groups,
  analysisDateUtc,
  page,
  pageSize,
  totalItems,
  totalPages,
  sorting,
  onSortingChange,
  onPageChange,
  onPageSizeChange,
}: {
  groups: ErrorGroup[];
  analysisDateUtc: string;
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  sorting: SortingState;
  onSortingChange: OnChangeFn<SortingState>;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}) {
```

Delete the old `const [sorting, setSorting] = useState<SortingState>(...)` line. Keep the `visibleColumns` state, its `useEffect`, and `handleColumnToggle`.

3) ID column offset — replace the `analysisIdentifier` `cell` (lines 138–141) with:

```tsx
        cell: (info) => {
          const visibleIndex = info.table.getRowModel().rows.findIndex((row) => row.id === info.row.id);
          const globalIndex = (page - 1) * pageSize + visibleIndex;
          return `${globalIndex + 1}-${formatAnalysisDate(analysisDateUtc)}`;
        },
```

Add `enableSorting: false` to the `analysisIdentifier` display column definition, and to the `message`, `serviceVersion`, `suggestion`, and `howToFix` accessor columns (these have no server-side sort key). Example for `message`:

```tsx
            columnHelper.accessor('message', {
              header: () => <ColumnHeader columnId="message">Message</ColumnHeader>,
              cell: (i) => <ExpandableCell value={i.getValue()} title="Message" />,
              enableSorting: false,
            }),
```

4) Add `page, pageSize` to the `columns` `useMemo` dependency array (so the ID offset recomputes): change `[analysisDateUtc, visibleColumns]` to `[analysisDateUtc, visibleColumns, page, pageSize]`.

5) Table config — replace the `useReactTable` call (lines 267–274):

```tsx
  const table = useReactTable({
    data: groups,
    columns,
    state: { sorting },
    onSortingChange,
    manualSorting: true,
    manualPagination: true,
    rowCount: totalItems,
    getCoreRowModel: getCoreRowModel(),
  });
```

6) Footer — add the `Pagination` right after the closing `</div>` of the table wrapper (after line 332, before the closing `</>`):

```tsx
        <Pagination
          page={page}
          pageSize={pageSize}
          totalItems={totalItems}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
```

- [ ] **Step 5: `TriageGroupsPanel` — data-fetching container (test first)**

Create `frontend/src/components/TriageGroupsPanel.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { TriageGroupsPanel } from './TriageGroupsPanel';
import * as api from '../api';
import type { ErrorGroup, PagedResult } from '../types';

vi.mock('../api');

const ONE: ErrorGroup = {
  severity: 'Error',
  count: 3,
  firstSeenUtc: '2026-06-19T08:00:00Z',
  lastSeenUtc: '2026-06-19T09:00:00Z',
  exceptionType: 'NullReferenceException',
  message: 'boom',
  endpoint: '/x',
  httpStatus: 500,
  serviceVersion: '1.0.0',
  correlationIdCount: 0,
  stackFile: null,
  stackSymbol: null,
  stackLine: null,
  suggestion: null,
  howToFix: null,
};

function page(items: ErrorGroup[], totalItems: number, p = 1): PagedResult<ErrorGroup> {
  return { items, page: p, pageSize: 25, totalItems, totalPages: Math.ceil(totalItems / 25) };
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TriageGroupsPanel runId="run-1" analysisDateUtc="2026-06-19T09:30:00Z" />
    </QueryClientProvider>,
  );
}

describe('<TriageGroupsPanel />', () => {
  beforeEach(() => vi.resetAllMocks());

  it('fetches the first page with the default sort and renders rows', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([ONE], 1));
    renderPanel();
    expect(await screen.findByTestId('triage-table')).toBeInTheDocument();
    expect(api.fetchGroupsPage).toHaveBeenCalledWith(
      'run-1',
      { page: 1, pageSize: 25, sort: 'severity', dir: 'desc' },
      expect.anything(),
    );
  });

  it('refetches page 2 when Next is clicked', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([ONE], 60));
    const { default: userEvent } = await import('@testing-library/user-event');
    const user = userEvent.setup();
    renderPanel();
    await screen.findByTestId('triage-table');
    await user.click(screen.getByRole('button', { name: /next/i }));
    await waitFor(() =>
      expect(api.fetchGroupsPage).toHaveBeenLastCalledWith(
        'run-1',
        { page: 2, pageSize: 25, sort: 'severity', dir: 'desc' },
        expect.anything(),
      ),
    );
  });

  it('shows an empty state when there are no groups', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([], 0));
    renderPanel();
    expect(await screen.findByText(/no error groups/i)).toBeInTheDocument();
  });
});
```

Create `frontend/src/components/TriageGroupsPanel.tsx`:

```tsx
import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import type { OnChangeFn, SortingState } from '@tanstack/react-table';
import { fetchGroupsPage } from '../api';
import type { GroupSort, SortDir } from '../types';
import { TriageTable } from './TriageTable';

const DEFAULT_PAGE_SIZE = 25;
const DEFAULT_SORTING: SortingState = [{ id: 'severity', desc: true }];

// TanStack column id → backend sort key. Columns absent here are not server-sortable.
const SORT_KEY_BY_COLUMN: Record<string, GroupSort> = {
  severity: 'severity',
  count: 'count',
  firstSeenUtc: 'firstSeen',
  lastSeenUtc: 'lastSeen',
  endpoint: 'endpoint',
  exceptionType: 'exceptionType',
  correlationIdCount: 'correlations',
};

export function TriageGroupsPanel({
  runId,
  analysisDateUtc,
}: {
  runId: string;
  analysisDateUtc: string;
}) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sorting, setSorting] = useState<SortingState>(DEFAULT_SORTING);

  const sort: GroupSort = SORT_KEY_BY_COLUMN[sorting[0]?.id ?? 'severity'] ?? 'severity';
  const dir: SortDir = sorting[0]?.desc === false ? 'asc' : 'desc';

  const { data, isLoading, error } = useQuery({
    queryKey: ['groups', runId, page, pageSize, sort, dir],
    queryFn: ({ signal }) => fetchGroupsPage(runId, { page, pageSize, sort, dir }, signal),
    placeholderData: keepPreviousData,
  });

  const handleSortingChange: OnChangeFn<SortingState> = updater => {
    setSorting(prev => (typeof updater === 'function' ? updater(prev) : updater));
    setPage(1);
  };

  function handlePageSizeChange(size: number) {
    setPageSize(size);
    setPage(1);
  }

  if (error) {
    return <p className="text-red-700">{(error as Error).message}</p>;
  }
  if (isLoading && !data) {
    return <p className="text-slate-500">Loading groups…</p>;
  }
  if (data && data.totalItems === 0) {
    return <p className="text-slate-600">No error groups in this window. 🎉</p>;
  }
  if (!data) return null;

  return (
    <TriageTable
      groups={data.items}
      analysisDateUtc={analysisDateUtc}
      page={data.page}
      pageSize={data.pageSize}
      totalItems={data.totalItems}
      totalPages={data.totalPages}
      sorting={sorting}
      onSortingChange={handleSortingChange}
      onPageChange={setPage}
      onPageSizeChange={handlePageSizeChange}
    />
  );
}
```

- [ ] **Step 6: `App.tsx` + `App.test.tsx` — wire header + container**

In `frontend/src/App.tsx`:

- Line 7–9 imports: add the container, drop the now-unused `TriageTable` import, and switch the type:
  ```tsx
  import { TriageGroupsPanel } from './components/TriageGroupsPanel';
  import { MetricsPanel } from './components/MetricsPanel';
  import type { TimeRangeSelection, TriageRunHeader } from './types';
  ```
  (Remove `import { TriageTable } from './components/TriageTable';`.)
- `useQuery<TriageResult>` (line 155) → `useQuery<TriageRunHeader>`.
- `useMutation<TriageResult, ...>` (line 162) → `useMutation<TriageRunHeader, Error, { apiName: string; range: TimeRangeSelection }>`.
- Header count (line 235): replace `{r.groups.length} group(s)` with `{r.totalGroups} group(s)`.
- MetricsPanel (line 265): `<MetricsPanel summary={r.summary} />`.
- Table (line 266): replace `<TriageTable groups={r.groups} analysisDateUtc={r.requestedAtUtc} />` with:
  ```tsx
  <TriageGroupsPanel runId={r.id} analysisDateUtc={r.requestedAtUtc} />
  ```

In `frontend/src/App.test.tsx`, update the `vi.mock('./api', …)` block so `fetchLatestTriage` returns a header and `fetchGroupsPage` is mocked:

```tsx
vi.mock('./api', () => ({
  fetchLatestTriage: vi.fn().mockResolvedValue({
    id: 'run-123',
    apiName: 'demo-api',
    requestedAtUtc: '2026-06-18T09:30:00Z',
    lookback: '24h',
    fromUtc: '2026-06-17T09:30:00Z',
    toUtc: '2026-06-18T09:30:00Z',
    isTruncated: false,
    totalLogs: 12,
    totalGroups: 0,
    summary: {
      totalGroups: 0,
      totalOccurrences: 0,
      fatal: 0,
      error: 0,
      warning: 0,
      withSuggestions: 0,
      withFixes: 0,
    },
  }),
  fetchTriageById: vi.fn(),
  fetchApiNames: vi.fn().mockResolvedValue(['demo-api', 'checkout-api']),
  runTriage: vi.fn(),
  fetchGroupsPage: vi.fn().mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 25,
    totalItems: 0,
    totalPages: 0,
  }),
}));
```

- [ ] **Step 7: Run the full frontend suite + lint**

```bash
npm --prefix frontend test -- --run
npm --prefix frontend run lint
```
Expected: PASS (all suites) and no lint errors. If lint flags an unused import (e.g. a leftover `TriageResult`), remove it.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/types.ts frontend/src/api.ts frontend/src/components/MetricsPanel.tsx frontend/src/components/MetricsPanel.test.tsx frontend/src/components/TriageTable.tsx frontend/src/components/TriageTable.test.tsx frontend/src/components/TriageGroupsPanel.tsx frontend/src/components/TriageGroupsPanel.test.tsx frontend/src/App.tsx frontend/src/App.test.tsx
git commit -m "Phase 13.B2: frontend server-side pagination (header + paged groups + numbered footer)"
```

---

## Task C1: End-to-end verification

**Files:** none (manual verification).

- [ ] **Step 1: Full gate**

```powershell
Get-Process -Name 'HotFixAmbulance.Api','demo-api' -ErrorAction SilentlyContinue | Stop-Process -Force
./.claude/hooks/pre-commit.ps1
```
Expected: all four gates `ok`.

- [ ] **Step 2: Drive the running app (uses the `verify` skill)**

Start the backend API and confirm the new contract against real data (Elastic/MSSQL/demo-api are already running from the earlier demo):

```bash
# In one shell: start the API
dotnet run --project backend/src/HotFixAmbulance.Api --urls http://localhost:5283 &
sleep 12
# Create a run, then page its groups
RUN=$(curl -s -X POST "http://localhost:5283/api/triage/demo-api?lookbackHours=24" | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
echo "run=$RUN"
curl -s "http://localhost:5283/api/triage/runs/$RUN/groups?page=1&pageSize=10&sort=severity&dir=desc" \
  | python3 -c "import sys,json;d=json.load(sys.stdin);print('page',d['page'],'size',d['pageSize'],'total',d['totalItems'],'pages',d['totalPages'],'items',len(d['items']))"
```
Expected: the POST response has `summary` + `totalGroups` and no `groups` array; the groups call prints `page 1 size 10 total <N> pages <ceil(N/10)> items <=10`.

- [ ] **Step 3: Browser check**

Open `http://localhost:5173`, run an analysis on `demo-api` over `24h`, and confirm: the table shows 25 rows, the footer reads "Showing 1–25 of N", clicking page 2 / changing rows-per-page updates the table, the metrics cards still show totals, and sorting a column header reorders across the whole set (not just the page).

- [ ] **Step 4: Stop the API**

```powershell
Get-Process -Name 'HotFixAmbulance.Api' -ErrorAction SilentlyContinue | Stop-Process -Force
```

---

## Self-review notes (resolved)

- **Spec coverage:** PagedResult/TriageSummary/TriageRunHeader (A1, A4); GroupPager summarize+paginate+sort+parse (A1, A2); paged endpoint + validation/404/out-of-range (A3); header endpoints (A4); frontend types/api/Pagination/TriageTable/container/MetricsPanel/App (B1, B2). Default 25 + numbered UI (B1). 400 vs graceful empty page (A2 test + A3 endpoint). All spec sections map to a task.
- **CLI untouched:** `TriageResult`/`TriageService.RunAsync` unchanged; only the HTTP layer maps to `TriageRunHeader`.
- **Type consistency:** `GroupSortKey`/`SortDirection` (backend) ↔ `GroupSort`/`SortDir` (frontend) bridged by `SORT_KEY_BY_COLUMN`; `fetchGroupsPage(runId, {page,pageSize,sort,dir}, signal)` signature matches the container call and the test assertion; `PagedResult`/`TriageSummary` field names match backend records (camelCased by the serializer).
- **Green per commit:** A1–A3 additive; A4 changes contract + its tests together; B1 additive; B2 migrates all interlocking FE files in one commit.
