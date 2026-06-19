import { useMemo, useState, useEffect } from 'react';
import {
  createColumnHelper,
  flexRender,
  getCoreRowModel,
  useReactTable,
  type OnChangeFn,
  type SortingState,
} from '@tanstack/react-table';
import {
  AlertCircle,
  Hash,
  Clock,
  Code2,
  MessageCircle,
  Route,
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
import { SeverityBadge } from './SeverityBadge';
import { ColumnSettingsModal } from './ColumnSettingsModal';
import { ExpandableCell } from './ExpandableCell';
import { Pagination } from './Pagination';

const columnHelper = createColumnHelper<ErrorGroup>();

function shortIso(value: string | null): string {
  if (!value) return '';
  return value.replace('T', ' ').replace(/\..*$/, '');
}

function formatAnalysisDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const day = String(date.getUTCDate()).padStart(2, '0');
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const year = String(date.getUTCFullYear());
  return `${day}${month}${year}`;
}

// Column metadata with icons and tooltips
const columnMetadata: Record<string, { icon: React.ElementType; tooltip: string }> = {
  analysisIdentifier: { icon: Hash, tooltip: 'Sequential row number and analysis date' },
  severity: { icon: AlertCircle, tooltip: 'Error severity level: Fatal, Error, or Warning' },
  count: { icon: Hash, tooltip: 'Number of occurrences of this error' },
  firstSeenUtc: { icon: Clock, tooltip: 'When this error was first detected' },
  lastSeenUtc: { icon: Clock, tooltip: 'Most recent occurrence of this error' },
  exceptionType: { icon: Code2, tooltip: 'Exception type with stack trace details' },
  message: { icon: MessageCircle, tooltip: 'Error message and context information' },
  endpoint: { icon: Route, tooltip: 'API endpoint where the error occurred' },
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
  serviceVersion: false,
  correlationIdCount: true,
  suggestion: true,
  howToFix: true,
};

/**
 * 13-column triage table: analysis identifier + 10 raw facts + Suggestion-for-Error + How-to-Fix.
 * Severity column is sorted by numeric rank so Fatal > Error > Warning.
 * Columns can be toggled via settings modal.
 */
export function TriageTable({
  groups,
  analysisDateUtc,
  page,
  pageSize,
  totalItems,
  totalPages,
  sorting,
  onSortingChange,
  onPageChange,
  onPageSizeChange,
}: {
  groups: ErrorGroup[];
  analysisDateUtc: string;
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  sorting: SortingState;
  onSortingChange: OnChangeFn<SortingState>;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}) {
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
      columnHelper.display({
        id: 'analysisIdentifier',
        header: () => <ColumnHeader columnId="analysisIdentifier">ID</ColumnHeader>,
        enableSorting: false,
        cell: (info) => {
          const globalIndex = (page - 1) * pageSize + info.row.index;
          return `${globalIndex + 1}-${formatAnalysisDate(analysisDateUtc)}`;
        },
      }),
      ...(visibleColumns.severity
        ? [
            columnHelper.accessor('severity', {
              header: () => <ColumnHeader columnId="severity">Severity</ColumnHeader>,
              cell: (info) => <SeverityBadge severity={info.getValue()} />,
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
              enableSorting: false,
              cell: (i) => <ExpandableCell value={i.getValue()} title="Message" />,
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
      ...(visibleColumns.serviceVersion
        ? [
            columnHelper.accessor('serviceVersion', {
              header: () => <ColumnHeader columnId="serviceVersion">Version</ColumnHeader>,
              enableSorting: false,
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
              enableSorting: false,
              cell: (i) => (
                <ExpandableCell
                  dataTestId="suggestion"
                  value={i.getValue()}
                  title="Suggestion for Error"
                  className="px-3 py-2 rounded bg-sky-50 text-slate-700 hover:bg-sky-100 transition-colors"
                />
              ),
            }),
          ]
        : []),
      ...(visibleColumns.howToFix
        ? [
            columnHelper.accessor('howToFix', {
              header: () => <ColumnHeader columnId="howToFix">How to fix</ColumnHeader>,
              enableSorting: false,
              cell: (i) => (
                <ExpandableCell
                  dataTestId="howtofix"
                  value={i.getValue()}
                  title="How to fix"
                  variant="mono"
                  className="px-3 py-2 rounded bg-emerald-50 text-slate-700 hover:bg-emerald-100 transition-colors"
                />
              ),
            }),
          ]
        : []),
    ],
    [analysisDateUtc, visibleColumns, page, pageSize],
  );

  const table = useReactTable({
    data: groups,
    columns,
    state: { sorting },
    onSortingChange,
    manualSorting: true,
    manualPagination: true,
    rowCount: totalItems,
    getCoreRowModel: getCoreRowModel(),
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
      <Pagination
        page={page}
        pageSize={pageSize}
        totalItems={totalItems}
        totalPages={totalPages}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
      />
    </>
  );
}
