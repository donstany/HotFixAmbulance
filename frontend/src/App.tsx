import { useEffect, useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { fetchLatestTriage, fetchTriageById } from './api';
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
  return (
    <div className="space-y-4">
      <header className="flex flex-wrap items-baseline gap-4">
        <h1 className="text-2xl font-bold text-slate-900">{r.apiName}</h1>
        <p className="text-sm text-slate-600">
          {r.totalLogs} log(s) in {r.groups.length} group(s) · run {new Date(r.requestedAtUtc).toLocaleString()}
        </p>
        <p className="ml-auto text-xs text-slate-400">analysis id {r.id}</p>
      </header>
      <MetricsPanel errorGroups={r.groups} />
      <TriageTable groups={r.groups} />
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
