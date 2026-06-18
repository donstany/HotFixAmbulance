import { useMemo, useState, useEffect } from 'react';
import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type SortingState,
} from '@tanstack/react-table';
import {
  AlertCircle,
  Hash,
  Clock,
  Code2,
  MessageCircle,
  Route,
  Network,
  Package,
  Link2,
  Lightbulb,
  Wrench,
  ChevronUp,
  ChevronDown,
  Settings,
  Info,
} from 'lucide-react';
import type { ErrorGroup } from '../types';
import { severityRank } from '../utils/severity';
import { SeverityBadge } from './SeverityBadge';
import { ColumnSettingsModal } from './ColumnSettingsModal';

const columnHelper = createColumnHelper<ErrorGroup>();

function shortIso(value: string | null): string {
  if (!value) return '';
  return value.replace('T', ' ').replace(/\..*$/, '');
}

function truncate(value: string | null, max = 80): string {
  if (!value) return '';
  return value.length > max ? `${value.slice(0, max)}…` : value;
}

// Column metadata with icons and tooltips
const columnMetadata: Record<string, { icon: React.ElementType; tooltip: string }> = {
  severity: { icon: AlertCircle, tooltip: 'Error severity level: Fatal, Error, or Warning' },
  count: { icon: Hash, tooltip: 'Number of occurrences of this error' },
  firstSeenUtc: { icon: Clock, tooltip: 'When this error was first detected' },
  lastSeenUtc: { icon: Clock, tooltip: 'Most recent occurrence of this error' },
  exceptionType: { icon: Code2, tooltip: 'Exception type with stack trace details' },
  message: { icon: MessageCircle, tooltip: 'Error message and context information' },
  endpoint: { icon: Route, tooltip: 'API endpoint where the error occurred' },
  httpStatus: { icon: Network, tooltip: 'HTTP status code returned' },
  serviceVersion: { icon: Package, tooltip: 'Service version when the error occurred' },
  correlationIdCount: { icon: Link2, tooltip: 'Number of unique request correlations' },
  suggestion: { icon: Lightbulb, tooltip: 'AI suggestion explaining what this error means' },
  howToFix: { icon: Wrench, tooltip: 'AI recommendation on how to fix this error' },
};

function ColumnIcon({ columnId }: { columnId: string }) {
  const metadata = columnMetadata[columnId];
  if (!metadata) return null;
  const { icon: IconComponent } = metadata;
  return <IconComponent size={16} className="inline mr-1.5" />;
}

function ColumnHeader({ columnId, children }: { columnId: string; children: React.ReactNode }) {
  const metadata = columnMetadata[columnId];
  const tooltip = metadata?.tooltip || '';

  return (
    <div className="flex items-center gap-1.5 group relative">
      <ColumnIcon columnId={columnId} />
      <span>{children}</span>
      {tooltip && (
        <div className="relative group/info">
          <Info size={14} className="text-slate-400 hover:text-slate-600 cursor-help flex-shrink-0" />
          {/* Tooltip - positioned right and visible on info icon hover */}
          <div className="invisible group-hover/info:visible absolute left-full top-1/2 transform -translate-y-1/2 ml-2 px-3 py-2 bg-slate-900 text-white text-xs rounded whitespace-nowrap z-50 pointer-events-none shadow-lg">
            {tooltip}
            <div className="absolute right-full top-1/2 transform -translate-y-1/2 border-4 border-transparent border-r-slate-900" />
          </div>
        </div>
      )}
    </div>
  );
}

const STORAGE_KEY = 'hotfixambulance_column_visibility';

const DEFAULT_VISIBLE_COLUMNS: Record<string, boolean> = {
  severity: true,
  count: true,
  firstSeenUtc: true,
  lastSeenUtc: true,
  exceptionType: true,
  message: true,
  endpoint: true,
  httpStatus: false,
  serviceVersion: false,
  correlationIdCount: true,
  suggestion: true,
  howToFix: true,
};

/**
 * 12-column triage table: 10 raw facts + Suggestion-for-Error + How-to-Fix.
 * Severity column is sorted by numeric rank so Fatal > Error > Warning.
 * Columns can be toggled via settings modal.
 */
export function TriageTable({ groups }: { groups: ErrorGroup[] }) {
  const [sorting, setSorting] = useState<SortingState>([{ id: 'severity', desc: true }]);
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? JSON.parse(stored) : DEFAULT_VISIBLE_COLUMNS;
  });
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(visibleColumns));
  }, [visibleColumns]);

  const handleColumnToggle = (columnId: string) => {
    setVisibleColumns((prev) => ({
      ...prev,
      [columnId]: !prev[columnId],
    }));
  };

  const columns = useMemo(
    () => [
      ...(visibleColumns.severity
        ? [
            columnHelper.accessor('severity', {
              header: () => <ColumnHeader columnId="severity">Severity</ColumnHeader>,
              cell: (info) => <SeverityBadge severity={info.getValue()} />,
              sortingFn: (a, b) => severityRank(a.original.severity) - severityRank(b.original.severity),
            }),
          ]
        : []),
      ...(visibleColumns.count
        ? [columnHelper.accessor('count', { header: () => <ColumnHeader columnId="count">Count</ColumnHeader> })]
        : []),
      ...(visibleColumns.firstSeenUtc
        ? [
            columnHelper.accessor('firstSeenUtc', {
              header: () => <ColumnHeader columnId="firstSeenUtc">First seen</ColumnHeader>,
              cell: (i) => shortIso(i.getValue()),
            }),
          ]
        : []),
      ...(visibleColumns.lastSeenUtc
        ? [
            columnHelper.accessor('lastSeenUtc', {
              header: () => <ColumnHeader columnId="lastSeenUtc">Last seen</ColumnHeader>,
              cell: (i) => shortIso(i.getValue()),
            }),
          ]
        : []),
      ...(visibleColumns.exceptionType
        ? [
            columnHelper.accessor('exceptionType', {
              header: () => <ColumnHeader columnId="exceptionType">Exception</ColumnHeader>,
              cell: (i) => {
                const row = i.row.original;
                const ex = i.getValue();
                const frame =
                  row.stackFile && row.stackLine
                    ? `${row.stackFile}:${row.stackLine}`
                    : row.stackFile ?? null;
                return (
                  <div className="space-y-0.5">
                    <div>{ex ?? '—'}</div>
                    {frame && (
                      <div
                        data-testid="stackframe"
                        className="font-mono text-[11px] text-slate-500"
                        title={row.stackSymbol ?? undefined}
                      >
                        {frame}
                        {row.stackSymbol ? ` · ${row.stackSymbol}` : ''}
                      </div>
                    )}
                  </div>
                );
              },
            }),
          ]
        : []),
      ...(visibleColumns.message
        ? [
            columnHelper.accessor('message', {
              header: () => <ColumnHeader columnId="message">Message</ColumnHeader>,
              cell: (i) => <span title={i.getValue() ?? ''}>{truncate(i.getValue())}</span>,
            }),
          ]
        : []),
      ...(visibleColumns.endpoint
        ? [
            columnHelper.accessor('endpoint', {
              header: () => <ColumnHeader columnId="endpoint">Endpoint</ColumnHeader>,
              cell: (i) => i.getValue() ?? '—',
            }),
          ]
        : []),
      ...(visibleColumns.httpStatus
        ? [
            columnHelper.accessor('httpStatus', {
              header: () => <ColumnHeader columnId="httpStatus">HTTP</ColumnHeader>,
              cell: (i) => i.getValue() ?? '—',
            }),
          ]
        : []),
      ...(visibleColumns.serviceVersion
        ? [
            columnHelper.accessor('serviceVersion', {
              header: () => <ColumnHeader columnId="serviceVersion">Version</ColumnHeader>,
              cell: (i) => i.getValue() ?? '—',
            }),
          ]
        : []),
      ...(visibleColumns.correlationIdCount
        ? [
            columnHelper.accessor('correlationIdCount', {
              header: () => <ColumnHeader columnId="correlationIdCount">Correlations</ColumnHeader>,
            }),
          ]
        : []),
      ...(visibleColumns.suggestion
        ? [
            columnHelper.accessor('suggestion', {
              header: () => <ColumnHeader columnId="suggestion">Suggestion for Error</ColumnHeader>,
              cell: (i) => (
                <span
                  data-testid="suggestion"
                  className="px-3 py-2 rounded bg-sky-50 text-slate-700 block hover:bg-sky-100 transition-colors"
                >
                  {i.getValue() ?? '—'}
                </span>
              ),
            }),
          ]
        : []),
      ...(visibleColumns.howToFix
        ? [
            columnHelper.accessor('howToFix', {
              header: () => <ColumnHeader columnId="howToFix">How to fix</ColumnHeader>,
              cell: (i) => (
                <pre
                  data-testid="howtofix"
                  className="whitespace-pre-wrap font-mono text-xs px-3 py-2 rounded bg-emerald-50 text-slate-700 hover:bg-emerald-100 transition-colors"
                >
                  {i.getValue() ?? '—'}
                </pre>
              ),
            }),
          ]
        : []),
    ],
    [visibleColumns],
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
    <>
      <div className="mb-4 flex justify-end">
        <button
          onClick={() => setShowSettings(true)}
          className="inline-flex items-center gap-2 px-3 py-2 rounded-lg border border-slate-300 bg-white hover:bg-slate-50 text-slate-700 text-sm font-medium transition-colors"
          title="Customize visible columns"
        >
          <Settings size={16} />
          Column Settings
        </button>
      </div>
      {showSettings && (
        <ColumnSettingsModal
          visibleColumns={visibleColumns}
          onToggle={handleColumnToggle}
          onClose={() => setShowSettings(false)}
          columnMetadata={columnMetadata}
        />
      )}
      <div className="overflow-x-auto rounded-lg border border-slate-200 shadow-sm">
        <table data-testid="triage-table" className="min-w-full border-collapse text-sm">
        <thead>
          {table.getHeaderGroups().map((hg) => (
            <tr key={hg.id} className="sticky top-0 z-10 border-b-2 border-slate-200 bg-gradient-to-r from-slate-50 to-slate-100">
              {hg.headers.map((h) => (
                <th
                  key={h.id}
                  onClick={h.column.getToggleSortingHandler()}
                  className="cursor-pointer px-4 py-3 text-left font-semibold text-slate-700 hover:bg-slate-200 transition-colors whitespace-nowrap relative"
                >
                  <div className="flex items-center gap-1">
                    {flexRender(h.column.columnDef.header, h.getContext())}
                    {h.column.getIsSorted() === 'asc' ? (
                      <ChevronUp size={14} className="ml-1" />
                    ) : h.column.getIsSorted() === 'desc' ? (
                      <ChevronDown size={14} className="ml-1" />
                    ) : null}
                  </div>
                </th>
              ))}
            </tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map((row) => (
            <tr key={row.id} className="border-b border-slate-200 align-top transition-colors hover:bg-slate-50">
              {row.getVisibleCells().map((cell) => (
                <td key={cell.id} className="px-4 py-3">
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
        </table>
      </div>
    </>
  );
}
