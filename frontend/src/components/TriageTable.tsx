import { useMemo } from 'react';
import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type SortingState,
} from '@tanstack/react-table';
import { useState } from 'react';
import type { ErrorGroup } from '../types';
import { severityRank } from '../utils/severity';
import { SeverityBadge } from './SeverityBadge';

const columnHelper = createColumnHelper<ErrorGroup>();

function shortIso(value: string | null): string {
  if (!value) return '';
  return value.replace('T', ' ').replace(/\..*$/, '');
}

function truncate(value: string | null, max = 80): string {
  if (!value) return '';
  return value.length > max ? `${value.slice(0, max)}…` : value;
}

/**
 * 12-column triage table: 10 raw facts + Purpose + How-to-Fix.
 * Severity column is sorted by numeric rank so Fatal > Error > Warning.
 */
export function TriageTable({ groups }: { groups: ErrorGroup[] }) {
  const [sorting, setSorting] = useState<SortingState>([{ id: 'severity', desc: true }]);

  const columns = useMemo(
    () => [
      columnHelper.accessor('severity', {
        header: 'Severity',
        cell: (info) => <SeverityBadge severity={info.getValue()} />,
        sortingFn: (a, b) => severityRank(a.original.severity) - severityRank(b.original.severity),
      }),
      columnHelper.accessor('count', { header: 'Count' }),
      columnHelper.accessor('firstSeenUtc', { header: 'First seen', cell: (i) => shortIso(i.getValue()) }),
      columnHelper.accessor('lastSeenUtc', { header: 'Last seen', cell: (i) => shortIso(i.getValue()) }),
      columnHelper.accessor('exceptionType', { header: 'Exception', cell: (i) => i.getValue() ?? '—' }),
      columnHelper.accessor('message', {
        header: 'Message',
        cell: (i) => <span title={i.getValue() ?? ''}>{truncate(i.getValue())}</span>,
      }),
      columnHelper.accessor('endpoint', { header: 'Endpoint', cell: (i) => i.getValue() ?? '—' }),
      columnHelper.accessor('httpStatus', { header: 'HTTP', cell: (i) => i.getValue() ?? '—' }),
      columnHelper.accessor('serviceVersion', { header: 'Version', cell: (i) => i.getValue() ?? '—' }),
      columnHelper.accessor('correlationIdCount', { header: 'Correlations' }),
      columnHelper.accessor('purpose', {
        header: 'Purpose',
        cell: (i) => <span data-testid="purpose">{i.getValue() ?? '—'}</span>,
      }),
      columnHelper.accessor('howToFix', {
        header: 'How to fix',
        cell: (i) => (
          <pre data-testid="howtofix" className="whitespace-pre-wrap font-mono text-xs">
            {i.getValue() ?? '—'}
          </pre>
        ),
      }),
    ],
    [],
  );

  const table = useReactTable({
    data: groups,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  });

  return (
    <table data-testid="triage-table" className="min-w-full border-collapse text-sm">
      <thead>
        {table.getHeaderGroups().map((hg) => (
          <tr key={hg.id} className="border-b bg-slate-100">
            {hg.headers.map((h) => (
              <th
                key={h.id}
                onClick={h.column.getToggleSortingHandler()}
                className="cursor-pointer px-2 py-2 text-left font-semibold text-slate-700"
              >
                {flexRender(h.column.columnDef.header, h.getContext())}
                {h.column.getIsSorted() === 'asc' ? ' ▲' : h.column.getIsSorted() === 'desc' ? ' ▼' : ''}
              </th>
            ))}
          </tr>
        ))}
      </thead>
      <tbody>
        {table.getRowModel().rows.map((row) => (
          <tr key={row.id} className="border-b align-top hover:bg-slate-50">
            {row.getVisibleCells().map((cell) => (
              <td key={cell.id} className="px-2 py-2">
                {flexRender(cell.column.columnDef.cell, cell.getContext())}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
