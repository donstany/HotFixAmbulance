import { describe, expect, it } from 'vitest';
import { severityRank, severityClasses } from './severity';

describe('severityRank', () => {
  it('ranks Fatal > Error > Warning', () => {
    expect(severityRank('Fatal')).toBeGreaterThan(severityRank('Error'));
    expect(severityRank('Error')).toBeGreaterThan(severityRank('Warning'));
  });

  it('returns 0 for unknown severities', () => {
    expect(severityRank('Trace')).toBe(0);
  });
});

describe('severityClasses', () => {
  it('returns distinct badge styles per severity', () => {
    const fatal = severityClasses('Fatal');
    const error = severityClasses('Error');
    const warning = severityClasses('Warning');
    expect(fatal).not.toBe(error);
    expect(error).not.toBe(warning);
    expect(fatal).toMatch(/red/);
    expect(warning).toMatch(/amber/);
  });

  it('returns a neutral style for unknown severities', () => {
    expect(severityClasses('Verbose')).toMatch(/gray/);
  });
});
