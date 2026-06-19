import type {
  ErrorGroup,
  GroupsPageRequest,
  PagedResult,
  TimeRangeSelection,
  TriageRunHeader,
} from './types';

const DEFAULT_BASE = '';

/** Resolves the API base URL: env var (`VITE_HFA_API`) or same-origin (proxied in dev). */
function baseUrl(): string {
  const fromEnv = import.meta.env.VITE_HFA_API as string | undefined;
  return fromEnv?.trim() || DEFAULT_BASE;
}

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

export async function fetchApiNames(signal?: AbortSignal): Promise<string[]> {
  const res = await fetch(`${baseUrl()}/api/apis`, { signal });
  if (!res.ok) {
    throw new Error(`GET /api/apis failed: ${res.status}`);
  }
  return (await res.json()) as string[];
}

/**
 * POSTs to /api/triage/{apiName} with either a relative (`lookbackHours`) or absolute
 * (`fromUtc`/`toUtc`) window. Returns the newly created TriageRunHeader.
 */
export async function runTriage(
  apiName: string,
  range: TimeRangeSelection,
  signal?: AbortSignal,
): Promise<TriageRunHeader> {
  const params = new URLSearchParams();
  if (range.kind === 'lookback') {
    params.set('lookbackHours', String(range.hours));
  } else {
    params.set('fromUtc', range.fromUtc);
    params.set('toUtc', range.toUtc);
  }
  const url = `${baseUrl()}/api/triage/${encodeURIComponent(apiName)}?${params.toString()}`;
  const res = await fetch(url, { method: 'POST', signal });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`POST /api/triage/${apiName} failed: ${res.status} ${body}`);
  }
  return (await res.json()) as TriageRunHeader;
}

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
