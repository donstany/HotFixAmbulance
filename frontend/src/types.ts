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
}

/** Mirrors backend `HotFixAmbulance.Api.TriageResult`. */
export interface TriageResult {
  id: string;
  apiName: string;
  requestedAtUtc: string;
  lookback: string;
  totalLogs: number;
  groups: ErrorGroup[];
}
