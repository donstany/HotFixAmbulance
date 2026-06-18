import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { TriageTable } from './TriageTable';
import type { ErrorGroup } from '../types';

const SAMPLE: ErrorGroup[] = [
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
];

describe('<TriageTable />', () => {
  it('renders all visible columns by default', () => {
    render(<TriageTable groups={SAMPLE} />);
    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(10);
  });

  it('sorts Fatal above Warning by default', () => {
    render(<TriageTable groups={SAMPLE} />);
    const rows = screen.getAllByRole('row').slice(1); // skip header
    const firstRowBadge = within(rows[0]).getByTestId('severity-badge');
    expect(firstRowBadge).toHaveTextContent('Fatal');
  });

  it('renders a "Suggestion for Error" column header', () => {
    render(<TriageTable groups={SAMPLE} />);
    expect(screen.getByRole('columnheader', { name: /suggestion for error/i })).toBeInTheDocument();
  });

  it('shows the suggestion and howToFix AI columns with distinct text', () => {
    render(<TriageTable groups={SAMPLE} />);
    expect(screen.getByText('OOM under load')).toBeInTheDocument();
    expect(screen.getByText(/beef456/)).toBeInTheDocument();
    // The two AI columns must render different content per row.
    const suggestions = screen.getAllByTestId('suggestion').map((el) => el.textContent);
    const fixes = screen.getAllByTestId('howtofix').map((el) => el.textContent);
    suggestions.forEach((s, i) => expect(s).not.toEqual(fixes[i]));
  });

  it('truncates long messages with a tooltip', () => {
    const long = 'x'.repeat(200);
    render(<TriageTable groups={[{ ...SAMPLE[0], message: long }]} />);
    const cell = screen.getByTitle(long);
    expect(cell.textContent ?? '').toHaveLength(81); // 80 chars + ellipsis
  });

  it('renders the stack frame (file:line · symbol) under the exception when present', () => {
    render(<TriageTable groups={SAMPLE} />);
    const frame = screen.getByTestId('stackframe');
    expect(frame.textContent).toMatch(/PaymentGateway\.cs:88/);
    expect(frame.textContent).toMatch(/PaymentGateway\.AuthorizeAsync/);
  });
});
