import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TimeRangePicker } from './TimeRangePicker';
import type { TimeRangeSelection } from '../types';

describe('<TimeRangePicker />', () => {
  it('emits a lookback selection when a preset button is clicked', async () => {
    const onChange = vi.fn();
    render(<TimeRangePicker onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: '6h' }));

    expect(onChange).toHaveBeenCalledWith({ kind: 'lookback', hours: 6 });
  });

  it('defaults the 24h preset to pressed when no value is provided', () => {
    render(<TimeRangePicker onChange={vi.fn()} />);
    const btn = screen.getByRole('button', { name: '24h' });
    expect(btn).toHaveAttribute('aria-pressed', 'true');
  });

  it('reveals custom from/to inputs when Custom is selected', async () => {
    render(<TimeRangePicker onChange={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Custom' }));
    expect(screen.getByLabelText(/from \(local time\)/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to \(local time\)/i)).toBeInTheDocument();
  });

  it('emits an absolute UTC selection when both custom inputs are filled', async () => {
    const onChange = vi.fn();
    render(<TimeRangePicker onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Custom' }));
    const from = screen.getByLabelText(/from \(local time\)/i);
    const to = screen.getByLabelText(/to \(local time\)/i);

    await userEvent.type(from, '2026-06-18T08:00');
    await userEvent.type(to, '2026-06-18T10:00');

    const calls = onChange.mock.calls.map(c => c[0] as TimeRangeSelection);
    const absolute = calls.find(c => c.kind === 'absolute');
    expect(absolute).toBeDefined();
    // `datetime-local` is interpreted in the browser's local timezone, then serialized to UTC.
    // We only assert shape + ordering since the absolute UTC depends on the test runner's tz.
    if (absolute && absolute.kind === 'absolute') {
      expect(Date.parse(absolute.fromUtc)).toBeLessThan(Date.parse(absolute.toUtc));
    }
  });

  it('shows an error when From is not earlier than To', async () => {
    render(<TimeRangePicker onChange={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Custom' }));
    const from = screen.getByLabelText(/from \(local time\)/i);
    const to = screen.getByLabelText(/to \(local time\)/i);
    await userEvent.type(from, '2026-06-18T10:00');
    await userEvent.type(to, '2026-06-18T08:00');

    expect(await screen.findByRole('alert')).toHaveTextContent(/earlier than to/i);
  });

  it('pre-fills custom inputs when an absolute value is supplied', () => {
    render(
      <TimeRangePicker
        onChange={vi.fn()}
        value={{ kind: 'absolute', fromUtc: '2026-06-18T08:00:00Z', toUtc: '2026-06-18T10:00:00Z' }}
      />,
    );
    expect(screen.getByRole('button', { name: 'Custom' })).toHaveAttribute('aria-pressed', 'true');
    const from = screen.getByLabelText(/from \(local time\)/i) as HTMLInputElement;
    expect(from.value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
  });
});
