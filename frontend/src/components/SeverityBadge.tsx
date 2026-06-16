import { severityClasses } from '../utils/severity';
import type { Severity } from '../types';

export function SeverityBadge({ severity }: { severity: Severity | string }) {
  return (
    <span
      data-testid="severity-badge"
      className={`inline-block rounded px-2 py-0.5 text-xs font-semibold uppercase ${severityClasses(severity)}`}
    >
      {severity}
    </span>
  );
}
