import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { MoreHorizontal, X } from 'lucide-react';

export type ExpandableCellVariant = 'plain' | 'mono';

interface ExpandableCellProps {
  /** Full value to display when expanded. Null/empty renders an em-dash. */
  value: string | null;
  /** Title shown in the modal header (e.g. column name). */
  title: string;
  /** Maximum number of lines shown inline before the cell is clamped. */
  maxLines?: number;
  /** Visual variant: 'plain' for prose, 'mono' for code/snippets (preserves whitespace). */
  variant?: ExpandableCellVariant;
  /** Optional test id forwarded to the inline preview span. */
  dataTestId?: string;
  /** Optional className applied to the inline wrapper (used for column-specific tinting). */
  className?: string;
}

/**
 * Character/newline fallback used when the browser cannot report layout overflow
 * (e.g. server render or jsdom). The live overflow check below is authoritative.
 */
function heuristicLong(text: string, maxLines: number): boolean {
  return text.length > 160 || text.split('\n').length > maxLines;
}

export function ExpandableCell({
  value,
  title,
  maxLines = 3,
  variant = 'plain',
  dataTestId,
  className,
}: ExpandableCellProps) {
  const [open, setOpen] = useState(false);
  const [clamped, setClamped] = useState(false);
  const previewRef = useRef<HTMLSpanElement>(null);
  const text = value ?? '';

  // Authoritative truncation check: is the rendered text actually clipped?
  useLayoutEffect(() => {
    const el = previewRef.current;
    if (!el) return;
    const measure = () => setClamped(el.scrollHeight - el.clientHeight > 1);
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, [text, maxLines]);

  if (!text) {
    return <span className={className}>—</span>;
  }

  const truncated = clamped || heuristicLong(text, maxLines);
  const baseClasses =
    variant === 'mono' ? 'font-mono text-xs whitespace-pre-wrap' : 'whitespace-pre-wrap';

  return (
    <>
      <div className={`max-w-md ${className ?? ''}`}>
        <span
          ref={previewRef}
          data-testid={dataTestId}
          onClick={truncated ? () => setOpen(true) : undefined}
          title={truncated ? `Click to read the full ${title}` : undefined}
          style={{
            display: '-webkit-box',
            WebkitLineClamp: maxLines,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
          }}
          className={`${baseClasses} block break-words leading-snug ${
            truncated ? 'cursor-pointer' : ''
          }`}
        >
          {text}
        </span>
        {truncated && (
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label={`Show full ${title}`}
            data-testid={dataTestId ? `${dataTestId}-more` : undefined}
            className="mt-1 inline-flex items-center gap-1 rounded text-[11px] font-medium text-sky-700 hover:text-sky-900 hover:underline"
          >
            <MoreHorizontal size={14} aria-hidden="true" />
            Show more
          </button>
        )}
      </div>
      {open && (
        <CellDetailModal title={title} value={text} variant={variant} onClose={() => setOpen(false)} />
      )}
    </>
  );
}

interface CellDetailModalProps {
  title: string;
  value: string;
  variant: ExpandableCellVariant;
  onClose: () => void;
}

function CellDetailModal({ title, value, variant, onClose }: CellDetailModalProps) {
  const [copied, setCopied] = useState(false);

  // Close on Escape — standard dialog affordance.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const bodyClasses =
    variant === 'mono'
      ? 'font-mono text-xs whitespace-pre-wrap break-words bg-slate-50 text-slate-800 p-4 rounded border border-slate-200'
      : 'whitespace-pre-wrap break-words text-sm text-slate-800 leading-relaxed';

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard may be unavailable in non-secure contexts; ignore silently.
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black/50 flex items-center justify-center z-[100] p-4"
      role="dialog"
      aria-modal="true"
      aria-label={title}
      onClick={onClose}
    >
      <div
        className="bg-white rounded-lg shadow-2xl max-w-4xl w-full max-h-[90vh] overflow-hidden flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="sticky top-0 bg-gradient-to-r from-slate-50 to-slate-100 px-6 py-4 border-b border-slate-200 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-slate-900">{title}</h2>
          <div className="flex items-center gap-2">
            <button
              onClick={handleCopy}
              className="px-3 py-1 text-sm rounded border border-slate-300 bg-white hover:bg-slate-50 text-slate-700 transition-colors"
              title="Copy full content to clipboard"
            >
              {copied ? 'Copied' : 'Copy'}
            </button>
            <button
              onClick={onClose}
              className="p-1 hover:bg-slate-200 rounded transition-colors"
              title="Close (Esc)"
              aria-label="Close"
            >
              <X size={20} className="text-slate-600" />
            </button>
          </div>
        </div>
        <div className="px-6 py-4 overflow-auto">
          <div className={bodyClasses}>{value}</div>
        </div>
      </div>
    </div>
  );
}
