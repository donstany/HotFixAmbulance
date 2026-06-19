import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import type { OnChangeFn, SortingState } from '@tanstack/react-table';
import { fetchGroupsPage } from '../api';
import type { GroupSort, SortDir } from '../types';
import { TriageTable } from './TriageTable';

const DEFAULT_PAGE_SIZE = 25;
const DEFAULT_SORTING: SortingState = [{ id: 'severity', desc: true }];

// TanStack column id → backend sort key. Columns absent here are not server-sortable.
const SORT_KEY_BY_COLUMN: Record<string, GroupSort> = {
  severity: 'severity',
  count: 'count',
  firstSeenUtc: 'firstSeen',
  lastSeenUtc: 'lastSeen',
  endpoint: 'endpoint',
  exceptionType: 'exceptionType',
  correlationIdCount: 'correlations',
};

export function TriageGroupsPanel({
  runId,
  analysisDateUtc,
}: {
  runId: string;
  analysisDateUtc: string;
}) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sorting, setSorting] = useState<SortingState>(DEFAULT_SORTING);

  const sort: GroupSort = SORT_KEY_BY_COLUMN[sorting[0]?.id ?? 'severity'] ?? 'severity';
  const dir: SortDir = sorting[0]?.desc === false ? 'asc' : 'desc';

  const { data, isLoading, error } = useQuery({
    queryKey: ['groups', runId, page, pageSize, sort, dir],
    queryFn: ({ signal }) => fetchGroupsPage(runId, { page, pageSize, sort, dir }, signal),
    placeholderData: keepPreviousData,
  });

  const handleSortingChange: OnChangeFn<SortingState> = updater => {
    setSorting(prev => (typeof updater === 'function' ? updater(prev) : updater));
    setPage(1);
  };

  function handlePageSizeChange(size: number) {
    setPageSize(size);
    setPage(1);
  }

  if (error) {
    return <p className="text-red-700">{(error as Error).message}</p>;
  }
  if (isLoading && !data) {
    return <p className="text-slate-500">Loading groups…</p>;
  }
  if (data && data.totalItems === 0) {
    return <p className="text-slate-600">No error groups in this window. 🎉</p>;
  }
  if (!data) return null;

  return (
    <TriageTable
      groups={data.items}
      analysisDateUtc={analysisDateUtc}
      page={data.page}
      pageSize={data.pageSize}
      totalItems={data.totalItems}
      totalPages={data.totalPages}
      sorting={sorting}
      onSortingChange={handleSortingChange}
      onPageChange={setPage}
      onPageSizeChange={handlePageSizeChange}
    />
  );
}
