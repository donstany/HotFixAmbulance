import type { Severity } from '../types';

const RANK: Record<Severity, number> = {
  Fatal: 3,
  Error: 2,
  Warning: 1,
};

/**
 * Numeric weight used to sort the triage table top-down. Higher = more urgent.
 * Unknown severities sort below Warning.
 */
export function severityRank(severity: Severity | string): number {
  return RANK[severity as Severity] ?? 0;
}

/** Tailwind class set for the severity badge. */
export function severityClasses(severity: Severity | string): string {
  switch (severity) {
    case 'Fatal':
      return 'bg-red-700 text-white';
    case 'Error':
      return 'bg-red-200 text-red-900';
    case 'Warning':
      return 'bg-amber-200 text-amber-900';
    default:
      return 'bg-gray-200 text-gray-700';
  }
}
