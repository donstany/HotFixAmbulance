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
    analyzedBy: null,
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
    analyzedBy: null,
  },
];

function groupWith(overrides: Partial<ErrorGroup>): ErrorGroup {
  return { ...SAMPLE[0], ...overrides };
}

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

  it('renders the Ollama badge on both AI columns when analyzedBy is Llm', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Llm' })] });
    expect(screen.getAllByTestId('ollama-badge')).toHaveLength(2);
  });

  it('does not render the Ollama badge when analyzedBy is Heuristic', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Heuristic' })] });
    expect(screen.queryByTestId('ollama-badge')).not.toBeInTheDocument();
  });

  it('does not render the Ollama badge when analyzedBy is null', () => {
    renderTable({ groups: [groupWith({ analyzedBy: null })] });
    expect(screen.queryByTestId('ollama-badge')).not.toBeInTheDocument();
  });
});
