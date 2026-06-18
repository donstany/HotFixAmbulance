import { useEffect, useState } from 'react';
import { QueryClient, QueryClientProvider, useMutation, useQuery } from '@tanstack/react-query';
import { AlertTriangle, CalendarRange, Play, RotateCcw, Sparkles } from 'lucide-react';
import { fetchApiNames, fetchLatestTriage, fetchTriageById, runTriage } from './api';
import { AnimatedAmbulanceIcon } from './components/AnimatedAmbulanceIcon';
import { TimeRangePicker } from './components/TimeRangePicker';
import { TriageTable } from './components/TriageTable';
import { MetricsPanel } from './components/MetricsPanel';
import type { TimeRangeSelection, TriageResult } from './types';

const client = new QueryClient({
  defaultOptions: { queries: { retry: 0, refetchOnWindowFocus: false } },
});

function readSearch() {
  const params = new URLSearchParams(window.location.search);
  return {
    analysisId: params.get('analysisId'),
    api: params.get('api'),
  };
}

function formatLocal(date: Date): string {
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatDuration(ms: number): string {
  if (ms < 60_000) return `${Math.max(1, Math.round(ms / 1000))}s`;
  const minutes = Math.round(ms / 60_000);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  const remMin = minutes % 60;
  if (hours < 24) return remMin === 0 ? `${hours}h` : `${hours}h ${remMin}m`;
  const days = Math.floor(hours / 24);
  const remHours = hours % 24;
  return remHours === 0 ? `${days}d` : `${days}d ${remHours}h`;
}

function formatWindow(fromUtc: string, toUtc: string): { from: string; to: string; duration: string } {
  const fromMs = Date.parse(fromUtc);
  const toMs = Date.parse(toUtc);
  return {
    from: formatLocal(new Date(fromMs)),
    to: formatLocal(new Date(toMs)),
    duration: formatDuration(Math.max(0, toMs - fromMs)),
  };
}

interface RunBarProps {
  initialApi: string | null;
  apiNames: string[];
  pendingApiNames: boolean;
  initialRange: TimeRangeSelection | undefined;
  rerunSignal: number;
  onRun: (apiName: string, range: TimeRangeSelection) => void;
  isRunning: boolean;
  runError: Error | null;
}

function RunBar({
  initialApi,
  apiNames,
  pendingApiNames,
  initialRange,
  rerunSignal,
  onRun,
  isRunning,
  runError,
}: RunBarProps) {
  const [apiName, setApiName] = useState<string>(initialApi ?? '');
  const [range, setRange] = useState<TimeRangeSelection>(
    initialRange ?? { kind: 'lookback', hours: 24 },
  );

  // Default the dropdown once api names load.
  useEffect(() => {
    if (!apiName && apiNames.length > 0) {
      setApiName(initialApi && apiNames.includes(initialApi) ? initialApi : apiNames[0]);
    }
  }, [apiNames, apiName, initialApi]);

  // Replay the parent-provided range whenever "Rerun this window" is clicked.
  useEffect(() => {
    if (initialRange) {
      setRange(initialRange);
    }
    // rerunSignal is the trigger; initialRange the payload.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rerunSignal]);

  function submit() {
    if (!apiName) return;
    onRun(apiName, range);
  }

  return (
    <section
      aria-label="Run analysis"
      className="flex flex-wrap items-end gap-4 rounded-2xl border border-slate-200 bg-white/80 px-4 py-3 shadow-sm backdrop-blur"
    >
      <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
        API
        <select
          aria-label="API"
          value={apiName}
          onChange={e => setApiName(e.target.value)}
          disabled={pendingApiNames || isRunning}
          className="min-w-[10rem] rounded border border-slate-300 px-2 py-1 text-sm text-slate-800 disabled:bg-slate-100"
        >
          {apiNames.length === 0 && <option value="">{pendingApiNames ? 'Loading…' : 'No APIs'}</option>}
          {apiNames.map(n => (
            <option key={n} value={n}>
              {n}
            </option>
          ))}
        </select>
      </label>
      <div className="flex-1 min-w-[16rem]">
        <TimeRangePicker value={range} onChange={setRange} disabled={isRunning} />
      </div>
      <button
        type="button"
        onClick={submit}
        disabled={isRunning || !apiName}
        className="inline-flex items-center gap-1.5 rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <Play size={14} aria-hidden="true" />
        {isRunning ? 'Running…' : 'Run analysis'}
      </button>
      {runError && (
        <p role="alert" className="basis-full text-xs font-medium text-rose-600">
          {runError.message}
        </p>
      )}
    </section>
  );
}

function TriageView() {
  const [{ analysisId, api }, setSearch] = useState(readSearch);
  const [rerunRange, setRerunRange] = useState<TimeRangeSelection | undefined>();
  const [rerunSignal, setRerunSignal] = useState(0);

  const apisQuery = useQuery<string[]>({
    queryKey: ['apis'],
    queryFn: ({ signal }) => fetchApiNames(signal),
  });

  const query = useQuery<TriageResult>({
    queryKey: ['triage', analysisId ?? api ?? ''],
    enabled: Boolean(analysisId || api),
    queryFn: ({ signal }) =>
      analysisId ? fetchTriageById(analysisId, signal) : fetchLatestTriage(api ?? '', signal),
  });

  const runMutation = useMutation<TriageResult, Error, { apiName: string; range: TimeRangeSelection }>({
    mutationFn: ({ apiName, range }) => runTriage(apiName, range),
    onSuccess: result => {
      // Navigate the URL to the freshly created run and refetch.
      const next = new URLSearchParams();
      next.set('analysisId', result.id);
      next.set('api', result.apiName);
      window.history.pushState({}, '', `?${next.toString()}`);
      setSearch({ analysisId: result.id, api: result.apiName });
    },
  });

  useEffect(() => {
    document.title = query.data ? `HotFixAmbulance · ${query.data.apiName}` : 'HotFixAmbulance';
  }, [query.data]);

  const r = query.data;

  function rerunCurrentWindow() {
    if (!r) return;
    setRerunRange({ kind: 'absolute', fromUtc: r.fromUtc, toUtc: r.toUtc });
    setRerunSignal(s => s + 1);
    runMutation.mutate({
      apiName: r.apiName,
      range: { kind: 'absolute', fromUtc: r.fromUtc, toUtc: r.toUtc },
    });
  }

  return (
    <div className="space-y-4">
      <RunBar
        initialApi={api}
        apiNames={apisQuery.data ?? []}
        pendingApiNames={apisQuery.isLoading}
        initialRange={rerunRange}
        rerunSignal={rerunSignal}
        onRun={(apiName, range) => runMutation.mutate({ apiName, range })}
        isRunning={runMutation.isPending}
        runError={runMutation.error}
      />

      {!analysisId && !api && !runMutation.isPending && (
        <p className="text-slate-600">
          No analysis selected. Pick an API and a time range above, then click <strong>Run analysis</strong>.
        </p>
      )}
      {query.isLoading && <p>Loading…</p>}
      {query.error && <p className="text-red-700">{(query.error as Error).message}</p>}

      {r && (
        <>
          <header className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-200 bg-white/80 px-4 py-3 shadow-sm backdrop-blur">
            <div className="flex flex-wrap items-center gap-3">
              <AnimatedAmbulanceIcon />
              <div>
                <div className="flex items-center gap-2">
                  <h1 className="text-2xl font-bold tracking-tight text-slate-900">{r.apiName}</h1>
                  <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-1 text-[11px] font-medium text-slate-600">
                    <Sparkles size={12} />
                    app context
                  </span>
                  {r.isTruncated && (
                    <span
                      role="status"
                      className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-1 text-[11px] font-medium text-amber-800 ring-1 ring-amber-300"
                      title="Elastic returned the MaxDocuments cap; results may be incomplete."
                    >
                      <AlertTriangle size={12} />
                      results truncated
                    </span>
                  )}
                </div>
                <p className="text-sm text-slate-600">
                  {r.totalLogs} log(s) in {r.groups.length} group(s) · run {new Date(r.requestedAtUtc).toLocaleString()}
                </p>
              </div>
            </div>
            {(() => {
              const w = formatWindow(r.fromUtc, r.toUtc);
              return (
                <p className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-full bg-amber-50 px-3 py-1.5 text-[11px] font-medium text-amber-800 ring-1 ring-amber-200">
                  <CalendarRange size={12} />
                  Logs from <span className="font-mono">{w.from}</span>
                  <span aria-hidden="true">→</span>
                  <span className="font-mono">{w.to}</span>
                  <span className="text-amber-700/70">({w.duration})</span>
                </p>
              );
            })()}
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={rerunCurrentWindow}
                disabled={runMutation.isPending}
                className="inline-flex items-center gap-1 rounded-md border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 shadow-sm transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                title="Re-run analysis with this exact window"
              >
                <RotateCcw size={12} aria-hidden="true" />
                Rerun this window
              </button>
              <p className="text-xs text-slate-400">analysis id {r.id}</p>
            </div>
          </header>
          <MetricsPanel errorGroups={r.groups} />
          <TriageTable groups={r.groups} analysisDateUtc={r.requestedAtUtc} />
        </>
      )}
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={client}>
      <main className="mx-auto max-w-screen-2xl space-y-4 p-6">
        <TriageView />
      </main>
    </QueryClientProvider>
  );
}
