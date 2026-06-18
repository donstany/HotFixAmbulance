import { useEffect, useState } from 'react';
import { Clock } from 'lucide-react';
import type { TimeRangeSelection } from '../types';

interface Preset {
  label: string;
  hours: number;
}

const PRESETS: readonly Preset[] = [
  { label: '15m', hours: 0.25 },
  { label: '1h', hours: 1 },
  { label: '6h', hours: 6 },
  { label: '24h', hours: 24 },
  { label: '7d', hours: 24 * 7 },
  { label: '30d', hours: 24 * 30 },
] as const;

const MAX_RANGE_DAYS = 30;
const CUSTOM = 'custom' as const;
type Selected = number | typeof CUSTOM;

interface TimeRangePickerProps {
  value?: TimeRangeSelection;
  onChange: (range: TimeRangeSelection) => void;
  disabled?: boolean;
}

/**
 * Converts a local datetime-input value (`YYYY-MM-DDTHH:mm`) interpreted in the browser's
 * local time zone into a UTC ISO-8601 string the backend can parse.
 */
function localInputToUtcIso(localValue: string): string {
  // The browser's `datetime-local` widget yields a naive string. `new Date(s)` interprets it
  // as local time, then `.toISOString()` re-emits it as UTC.
  return new Date(localValue).toISOString();
}

/**
 * Converts a UTC ISO-8601 timestamp into the `YYYY-MM-DDTHH:mm` form expected by
 * `<input type="datetime-local">` in the browser's local timezone.
 */
function utcIsoToLocalInput(utcIso: string): string {
  const d = new Date(utcIso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function pickInitialSelected(value?: TimeRangeSelection): Selected {
  if (!value) return 24;
  if (value.kind === 'lookback') {
    return PRESETS.find(p => p.hours === value.hours)?.hours ?? CUSTOM;
  }
  return CUSTOM;
}

export function TimeRangePicker({ value, onChange, disabled }: TimeRangePickerProps) {
  const [selected, setSelected] = useState<Selected>(() => pickInitialSelected(value));
  const [customFrom, setCustomFrom] = useState<string>(() =>
    value?.kind === 'absolute' ? utcIsoToLocalInput(value.fromUtc) : '',
  );
  const [customTo, setCustomTo] = useState<string>(() =>
    value?.kind === 'absolute' ? utcIsoToLocalInput(value.toUtc) : '',
  );
  const [error, setError] = useState<string | null>(null);

  // Sync inbound value (e.g. when "Rerun this window" pre-fills the picker on a historical run).
  useEffect(() => {
    if (!value) return;
    setSelected(pickInitialSelected(value));
    if (value.kind === 'absolute') {
      setCustomFrom(utcIsoToLocalInput(value.fromUtc));
      setCustomTo(utcIsoToLocalInput(value.toUtc));
    }
  }, [value]);

  function selectPreset(hours: number) {
    setSelected(hours);
    setError(null);
    onChange({ kind: 'lookback', hours });
  }

  function selectCustom() {
    setSelected(CUSTOM);
    if (customFrom && customTo) {
      validateAndEmit(customFrom, customTo);
    }
  }

  function validateAndEmit(fromLocal: string, toLocal: string) {
    if (!fromLocal || !toLocal) {
      setError('Pick both from and to.');
      return;
    }
    const fromUtcIso = localInputToUtcIso(fromLocal);
    const toUtcIso = localInputToUtcIso(toLocal);
    const fromMs = Date.parse(fromUtcIso);
    const toMs = Date.parse(toUtcIso);
    if (Number.isNaN(fromMs) || Number.isNaN(toMs)) {
      setError('Invalid date.');
      return;
    }
    if (fromMs >= toMs) {
      setError('From must be earlier than To.');
      return;
    }
    const spanMs = toMs - fromMs;
    if (spanMs > MAX_RANGE_DAYS * 24 * 60 * 60 * 1000) {
      setError(`Range exceeds ${MAX_RANGE_DAYS}-day cap.`);
      return;
    }
    setError(null);
    onChange({ kind: 'absolute', fromUtc: fromUtcIso, toUtc: toUtcIso });
  }

  function onFromChange(v: string) {
    setCustomFrom(v);
    setSelected(CUSTOM);
    validateAndEmit(v, customTo);
  }

  function onToChange(v: string) {
    setCustomTo(v);
    setSelected(CUSTOM);
    validateAndEmit(customFrom, v);
  }

  return (
    <div className="flex flex-col gap-2" role="group" aria-label="Time range">
      <div className="flex flex-wrap items-center gap-1">
        <span className="inline-flex items-center gap-1 pr-1 text-xs font-medium text-slate-500">
          <Clock size={12} aria-hidden="true" />
          Range
        </span>
        {PRESETS.map(p => {
          const isActive = selected === p.hours;
          return (
            <button
              key={p.label}
              type="button"
              disabled={disabled}
              aria-pressed={isActive}
              onClick={() => selectPreset(p.hours)}
              className={`rounded-full px-2.5 py-1 text-xs font-medium transition disabled:cursor-not-allowed disabled:opacity-50 ${
                isActive
                  ? 'bg-slate-900 text-white shadow-sm'
                  : 'bg-slate-100 text-slate-700 hover:bg-slate-200'
              }`}
            >
              {p.label}
            </button>
          );
        })}
        <button
          type="button"
          disabled={disabled}
          aria-pressed={selected === CUSTOM}
          onClick={selectCustom}
          className={`rounded-full px-2.5 py-1 text-xs font-medium transition disabled:cursor-not-allowed disabled:opacity-50 ${
            selected === CUSTOM
              ? 'bg-slate-900 text-white shadow-sm'
              : 'bg-slate-100 text-slate-700 hover:bg-slate-200'
          }`}
        >
          Custom
        </button>
      </div>
      {selected === CUSTOM && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-slate-600">
          <label className="flex items-center gap-1">
            <span className="text-slate-500">From</span>
            <input
              type="datetime-local"
              value={customFrom}
              onChange={e => onFromChange(e.target.value)}
              disabled={disabled}
              aria-label="From (local time)"
              className="rounded border border-slate-300 px-2 py-1 font-mono text-xs disabled:bg-slate-100"
            />
          </label>
          <label className="flex items-center gap-1">
            <span className="text-slate-500">To</span>
            <input
              type="datetime-local"
              value={customTo}
              onChange={e => onToChange(e.target.value)}
              disabled={disabled}
              aria-label="To (local time)"
              className="rounded border border-slate-300 px-2 py-1 font-mono text-xs disabled:bg-slate-100"
            />
          </label>
          <span className="text-[10px] text-slate-400">(local time, sent as UTC)</span>
          {error && (
            <span role="alert" className="text-[11px] font-medium text-rose-600">
              {error}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
