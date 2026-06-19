import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import App from './App';

vi.mock('./api', () => ({
  fetchLatestTriage: vi.fn().mockResolvedValue({
    id: 'run-123',
    apiName: 'demo-api',
    requestedAtUtc: '2026-06-18T09:30:00Z',
    lookback: '24h',
    fromUtc: '2026-06-17T09:30:00Z',
    toUtc: '2026-06-18T09:30:00Z',
    isTruncated: false,
    totalLogs: 12,
    totalGroups: 0,
    summary: {
      totalGroups: 0,
      totalOccurrences: 0,
      fatal: 0,
      error: 0,
      warning: 0,
      withSuggestions: 0,
      withFixes: 0,
    },
  }),
  fetchTriageById: vi.fn(),
  fetchApiNames: vi.fn().mockResolvedValue(['demo-api', 'checkout-api']),
  runTriage: vi.fn(),
  fetchGroupsPage: vi.fn().mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 25,
    totalItems: 0,
    totalPages: 0,
  }),
}));

describe('<App />', () => {
  it('renders a branded animated ambulance icon next to the demo api name', async () => {
    window.history.pushState({}, '', '/?api=demo-api');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'demo-api' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /ambulance/i })).toHaveClass(/animate-|motion-|spin|pulse/);
  });
});