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
