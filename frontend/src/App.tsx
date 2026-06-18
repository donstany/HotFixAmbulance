import { useEffect, useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { Sparkles, CalendarRange } from 'lucide-react';
import { fetchLatestTriage, fetchTriageById } from './api';
import { AnimatedAmbulanceIcon } from './components/AnimatedAmbulanceIcon';
import { TriageTable } from './components/TriageTable';
import { MetricsPanel } from './components/MetricsPanel';
import type { TriageResult } from './types';

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

function computeLogRange(r: TriageResult): { from: string; to: string; duration: string } | null {
  const stamps: number[] = [];
  for (const g of r.groups) {
    const first = Date.parse(g.firstSeenUtc);
    const last = Date.parse(g.lastSeenUtc);
    if (!Number.isNaN(first)) stamps.push(first);
    if (!Number.isNaN(last)) stamps.push(last);
  }
  if (stamps.length === 0) return null;
  const min = Math.min(...stamps);
  const max = Math.max(...stamps);
  return {
    from: formatLocal(new Date(min)),
    to: formatLocal(new Date(max)),
    duration: formatDuration(Math.max(0, max - min)),
  };
}

function TriageView() {
  const [{ analysisId, api }] = useState(readSearch);

  const query = useQuery<TriageResult>({
    queryKey: ['triage', analysisId ?? api ?? ''],
    enabled: Boolean(analysisId || api),
    queryFn: ({ signal }) =>
      analysisId ? fetchTriageById(analysisId, signal) : fetchLatestTriage(api ?? '', signal),
  });

  useEffect(() => {
    document.title = query.data ? `HotFixAmbulance · ${query.data.apiName}` : 'HotFixAmbulance';
  }, [query.data]);

  if (!analysisId && !api) {
    return (
      <p className="text-slate-600">
        No analysis selected. Run <code>/hot-fix-ambulance &lt;apiName&gt;</code> first, or pass <code>?api=...</code> in the URL.
      </p>
    );
  }
  if (query.isLoading) return <p>Loading…</p>;
  if (query.error) return <p className="text-red-700">{(query.error as Error).message}</p>;
  if (!query.data) return null;

  const r = query.data;
  const logRange = computeLogRange(r);
  return (
    <div className="space-y-4">
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
            </div>
            <p className="text-sm text-slate-600">
              {r.totalLogs} log(s) in {r.groups.length} group(s) · run {new Date(r.requestedAtUtc).toLocaleString()}
            </p>
          </div>
        </div>
        {logRange && (
          <p className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-full bg-amber-50 px-3 py-1.5 text-[11px] font-medium text-amber-800 ring-1 ring-amber-200">
            <CalendarRange size={12} />
            Logs from <span className="font-mono">{logRange.from}</span>
            <span aria-hidden="true">→</span>
            <span className="font-mono">{logRange.to}</span>
            <span className="text-amber-700/70">({logRange.duration})</span>
          </p>
        )}
        <p className="text-xs text-slate-400">analysis id {r.id}</p>
      </header>
      <MetricsPanel errorGroups={r.groups} />
      <TriageTable groups={r.groups} analysisDateUtc={r.requestedAtUtc} />
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
