import { ChevronLeft, ChevronRight } from 'lucide-react';

/** Build a compact page list with ellipses: e.g. pageList(6, 20) → [1,'ellipsis',5,6,7,'ellipsis',20]. */
export function pageList(current: number, total: number): (number | 'ellipsis')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | 'ellipsis')[] = [1];
  // Keep a window of neighbours around the current page, clamped so the run
  // near either edge stays a few pages wide (so e.g. page 1 still surfaces 2-3).
  let start = Math.max(2, current - 1);
  let end = Math.min(total - 1, current + 1);
  if (current <= 3) end = Math.min(total - 1, 4);
  if (current >= total - 2) start = Math.max(2, total - 3);
  if (start > 2) pages.push('ellipsis');
  for (let p = start; p <= end; p++) pages.push(p);
  if (end < total - 1) pages.push('ellipsis');
  pages.push(total);
  return pages;
}

interface PaginationProps {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  pageSizeOptions?: number[];
}

export function Pagination({
  page,
  pageSize,
  totalItems,
  totalPages,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 25, 50, 100],
}: PaginationProps) {
  const from = totalItems === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalItems);

  return (
    <nav
      aria-label="Pagination"
      className="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-slate-600"
    >
      <div className="flex items-center gap-3">
        <span className="text-slate-800">{`Showing ${from}–${to} of ${totalItems}`}</span>
        <label className="flex items-center gap-1.5">
          <span className="text-slate-500">Rows per page</span>
          <select
            aria-label="Rows per page"
            value={pageSize}
            onChange={e => onPageSizeChange(Number(e.target.value))}
            className="rounded border border-slate-300 px-2 py-1 text-sm"
          >
            {pageSizeOptions.map(o => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          className="inline-flex items-center gap-1 rounded-md border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <ChevronLeft size={14} aria-hidden="true" />
          Prev
        </button>

        {pageList(page, Math.max(totalPages, 1)).map((p, i) =>
          p === 'ellipsis' ? (
            <span key={`e${i}`} className="px-2 text-slate-400" aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              onClick={() => onPageChange(p)}
              aria-current={p === page ? 'page' : undefined}
              className={`min-w-[2rem] rounded-md px-2 py-1 text-xs font-medium transition ${
                p === page
                  ? 'bg-slate-900 text-white shadow-sm'
                  : 'border border-slate-300 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              {p}
            </button>
          ),
        )}

        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages}
          className="inline-flex items-center gap-1 rounded-md border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Next
          <ChevronRight size={14} aria-hidden="true" />
        </button>
      </div>
    </nav>
  );
}
