import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import App from './App';

vi.mock('./api', () => ({
  fetchLatestTriage: vi.fn().mockResolvedValue({
    id: 'run-123',
    apiName: 'demo-api',
    requestedAtUtc: '2026-06-18T09:30:00Z',
    lookback: '24h',
    totalLogs: 12,
    groups: [],
  }),
  fetchTriageById: vi.fn(),
}));

describe('<App />', () => {
  it('renders a branded animated ambulance icon next to the demo api name', async () => {
    window.history.pushState({}, '', '/?api=demo-api');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'demo-api' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /ambulance/i })).toHaveClass(/animate-|motion-|spin|pulse/);
  });
});