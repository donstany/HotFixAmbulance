import { severityClasses, getSeverityIcon } from '../utils/severity';
import type { Severity } from '../types';

export function SeverityBadge({ severity }: { severity: Severity | string }) {
  const IconComponent = getSeverityIcon(severity);

  return (
    <span
      data-testid="severity-badge"
      className={`inline-flex items-center gap-1 rounded px-3 py-1.5 text-xs font-semibold uppercase ${severityClasses(severity)}`}
    >
      <IconComponent size={14} />
      {severity}
    </span>
  );
}
