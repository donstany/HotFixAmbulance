export type Severity = 'Warning' | 'Error' | 'Fatal';

/** Mirrors backend `HotFixAmbulance.Core.ErrorGroup`. */
export interface ErrorGroup {
  severity: Severity;
  count: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  exceptionType: string | null;
  message: string | null;
  endpoint: string | null;
  httpStatus: number | null;
  serviceVersion: string | null;
  correlationIdCount: number;
  /** Topmost user-code frame, used by the UI's exception cell and the git-evidence column. */
  stackFile: string | null;
  stackSymbol: string | null;
  stackLine: number | null;
  /** Renders in the UI's "Suggestion for Error" column. WHAT the error means. */
  suggestion: string | null;
  /** Renders in the UI's "How to fix" column. HOW to remediate it. */
  howToFix: string | null;
  /** Which strategy wrote the AI columns: "Llm", "Heuristic", or null for legacy runs. */
  analyzedBy: string | null;
}

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
  fromUtc: string;
  toUtc: string;
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

/**
 * Time-range descriptor emitted by the TimeRangePicker. `lookback` runs in relative mode
 * (anchored at request time on the backend); `absolute` carries a user-picked UTC range.
 */
export type TimeRangeSelection =
  | { kind: 'lookback'; hours: number }
  | { kind: 'absolute'; fromUtc: string; toUtc: string };
