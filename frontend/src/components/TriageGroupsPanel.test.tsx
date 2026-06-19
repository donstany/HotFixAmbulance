import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { TriageGroupsPanel } from './TriageGroupsPanel';
import * as api from '../api';
import type { ErrorGroup, PagedResult } from '../types';

vi.mock('../api');

const ONE: ErrorGroup = {
  severity: 'Error',
  count: 3,
  firstSeenUtc: '2026-06-19T08:00:00Z',
  lastSeenUtc: '2026-06-19T09:00:00Z',
  exceptionType: 'NullReferenceException',
  message: 'boom',
  endpoint: '/x',
  httpStatus: 500,
  serviceVersion: '1.0.0',
  correlationIdCount: 0,
  stackFile: null,
  stackSymbol: null,
  stackLine: null,
  suggestion: null,
  howToFix: null,
};

function page(items: ErrorGroup[], totalItems: number, p = 1): PagedResult<ErrorGroup> {
  return { items, page: p, pageSize: 25, totalItems, totalPages: Math.ceil(totalItems / 25) };
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TriageGroupsPanel runId="run-1" analysisDateUtc="2026-06-19T09:30:00Z" />
    </QueryClientProvider>,
  );
}

describe('<TriageGroupsPanel />', () => {
  beforeEach(() => vi.resetAllMocks());

  it('fetches the first page with the default sort and renders rows', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([ONE], 1));
    renderPanel();
    expect(await screen.findByTestId('triage-table')).toBeInTheDocument();
    expect(api.fetchGroupsPage).toHaveBeenCalledWith(
      'run-1',
      { page: 1, pageSize: 25, sort: 'severity', dir: 'desc' },
      expect.anything(),
    );
  });

  it('refetches page 2 when Next is clicked', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([ONE], 60));
    const { default: userEvent } = await import('@testing-library/user-event');
    const user = userEvent.setup();
    renderPanel();
    await screen.findByTestId('triage-table');
    await user.click(screen.getByRole('button', { name: /next/i }));
    await waitFor(() =>
      expect(api.fetchGroupsPage).toHaveBeenLastCalledWith(
        'run-1',
        { page: 2, pageSize: 25, sort: 'severity', dir: 'desc' },
        expect.anything(),
      ),
    );
  });

  it('shows an empty state when there are no groups', async () => {
    vi.mocked(api.fetchGroupsPage).mockResolvedValue(page([], 0));
    renderPanel();
    expect(await screen.findByText(/no error groups/i)).toBeInTheDocument();
  });
});
