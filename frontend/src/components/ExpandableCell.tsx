import { useState } from 'react';
import { MoreHorizontal, X } from 'lucide-react';

export type ExpandableCellVariant = 'plain' | 'mono';

interface ExpandableCellProps {
  /** Full value to display when expanded. Null/empty renders an em-dash. */
  value: string | null;
  /** Title shown in the modal header (e.g. column name). */
  title: string;
  /** Truncation threshold in characters for the inline preview (single-line content). */
  previewLength?: number;
  /** Maximum number of newline-delimited lines kept in the preview before truncating. */
  maxLines?: number;
  /** Visual variant: 'plain' for prose, 'mono' for code/snippets (preserves whitespace). */
  variant?: ExpandableCellVariant;
  /** Optional test id forwarded to the inline preview span (kept for backwards-compatible tests). */
  dataTestId?: string;
  /** Optional className applied to the inline wrapper (used for column-specific tinting). */
  className?: string;
}

function isLong(value: string, previewLength: number, maxLines: number): boolean {
  if (value.length > previewLength) return true;
  return value.split('\n').length > maxLines;
}

function buildPreview(value: string, previewLength: number, maxLines: number): string {
  const lines = value.split('\n');
  if (lines.length > maxLines) {
    return `${lines.slice(0, maxLines).join('\n')}…`;
  }
  if (value.length > previewLength) {
    return `${value.slice(0, previewLength)}…`;
  }
  return value;
}

export function ExpandableCell({
  value,
  title,
  previewLength = 240,
  maxLines = 3,
  variant = 'plain',
  dataTestId,
  className,
}: ExpandableCellProps) {
  const [open, setOpen] = useState(false);
  const text = value ?? '';

  if (!text) {
    return <span className={className}>—</span>;
  }

  const long = isLong(text, previewLength, maxLines);
  const preview = buildPreview(text, previewLength, maxLines);

  const baseClasses =
    variant === 'mono' ? 'font-mono text-xs whitespace-pre-wrap' : 'whitespace-pre-wrap';

  return (
    <>
      <div className={`flex items-start gap-2 max-w-md ${className ?? ''}`}>
        <span
          data-testid={dataTestId}
          title={long ? text : undefined}
          className={`${baseClasses} flex-1 min-w-0 line-clamp-3 break-words leading-snug`}
        >
          {preview}
        </span>
        {long && (
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label={`Show full ${title}`}
            title={`Show full ${title}`}
            className="flex-shrink-0 p-1 rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800 transition-colors"
          >
            <MoreHorizontal size={16} />
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
  const bodyClasses =
    variant === 'mono'
      ? 'font-mono text-xs whitespace-pre-wrap break-words bg-slate-50 text-slate-800 p-4 rounded border border-slate-200'
      : 'whitespace-pre-wrap break-words text-sm text-slate-800 leading-relaxed';

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
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
              Copy
            </button>
            <button
              onClick={onClose}
              className="p-1 hover:bg-slate-200 rounded transition-colors"
              title="Close"
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
