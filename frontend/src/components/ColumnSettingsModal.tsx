import { X, Check } from 'lucide-react';
import React from 'react';

interface ColumnMetadata {
  icon: React.ElementType;
  tooltip: string;
}

interface ColumnSettingsModalProps {
  visibleColumns: Record<string, boolean>;
  onToggle: (columnId: string) => void;
  onClose: () => void;
  columnMetadata: Record<string, ColumnMetadata>;
}

const columnLabels: Record<string, string> = {
  severity: 'Severity',
  count: 'Count',
  firstSeenUtc: 'First Seen',
  lastSeenUtc: 'Last Seen',
  exceptionType: 'Exception Type',
  message: 'Message',
  endpoint: 'Endpoint',
  serviceVersion: 'Service Version',
  correlationIdCount: 'Correlations',
  suggestion: 'Suggestion for Error',
  howToFix: 'How to Fix',
};

export function ColumnSettingsModal({
  visibleColumns,
  onToggle,
  onClose,
  columnMetadata,
}: ColumnSettingsModalProps) {
  const columns = Object.entries(columnMetadata);

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[100] p-4">
      <div className="bg-white rounded-lg shadow-2xl max-w-md w-full max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="sticky top-0 bg-gradient-to-r from-slate-50 to-slate-100 px-6 py-4 border-b border-slate-200 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-slate-900">Column Visibility</h2>
          <button
            onClick={onClose}
            className="p-1 hover:bg-slate-200 rounded transition-colors"
            title="Close"
          >
            <X size={20} className="text-slate-600" />
          </button>
        </div>

        {/* Column List */}
        <div className="overflow-y-auto flex-1 px-6 py-4">
          <div className="space-y-3">
            {columns.map(([columnId, metadata]) => (
              <div key={columnId} className="flex items-start gap-3 p-3 rounded-lg hover:bg-slate-50 transition-colors">
                {/* Checkbox */}
                <button
                  onClick={() => onToggle(columnId)}
                  className={`mt-1 w-5 h-5 rounded border-2 flex items-center justify-center transition-all flex-shrink-0 ${
                    visibleColumns[columnId]
                      ? 'bg-blue-500 border-blue-500'
                      : 'border-slate-300 hover:border-slate-400'
                  }`}
                  title={`Toggle ${columnLabels[columnId] || columnId}`}
                >
                  {visibleColumns[columnId] && <Check size={14} className="text-white" />}
                </button>

                {/* Label and Tooltip */}
                <div className="flex-1 min-w-0">
                  <div className="font-medium text-slate-900">{columnLabels[columnId] || columnId}</div>
                  <div className="text-xs text-slate-600 mt-1 line-clamp-2">{metadata.tooltip}</div>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 bg-slate-50 px-6 py-4 border-t border-slate-200 flex gap-3">
          <button
            onClick={onClose}
            className="flex-1 px-4 py-2 rounded-lg border border-slate-300 text-slate-700 font-medium hover:bg-slate-100 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
