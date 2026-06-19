import { AlertTriangle, Lightbulb, Wrench } from 'lucide-react';
import type { TriageSummary } from '../types';

interface MetricsPanelProps {
  summary: TriageSummary;
}

export function MetricsPanel({ summary }: MetricsPanelProps) {
  const totalErrors = summary.totalOccurrences;
  const withSuggestions = summary.withSuggestions;
  const withFixes = summary.withFixes;

  const metrics = [
    {
      id: 'total-errors',
      label: 'Total Errors Detected',
      value: totalErrors.toLocaleString('en-US'),
      icon: AlertTriangle,
      color: 'from-rose-50 to-pink-50',
      iconColor: 'text-rose-600',
      bgIcon: 'bg-rose-100',
      tooltip: 'Total occurrences of all errors across your logs. Helps you understand the volume of issues your application is experiencing.',
    },
    {
      id: 'ai-insights',
      label: 'AI Insights Generated',
      value: withSuggestions,
      icon: Lightbulb,
      color: 'from-amber-50 to-yellow-50',
      iconColor: 'text-amber-600',
      bgIcon: 'bg-amber-100',
      tooltip: 'Number of error groups analyzed by AI that received intelligent suggestions. These insights explain what each error means and its root cause.',
    },
    {
      id: 'quick-fixes',
      label: 'Fix Recommendations',
      value: withFixes,
      icon: Wrench,
      color: 'from-teal-50 to-emerald-50',
      iconColor: 'text-teal-600',
      bgIcon: 'bg-teal-100',
      tooltip: 'Number of errors with actionable fix recommendations from AI. These suggest concrete steps to resolve each issue quickly.',
    },
  ];

  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
      {metrics.map(metric => {
        const Icon = metric.icon;
        return (
          <div
            key={metric.id}
            className={`bg-gradient-to-br ${metric.color} rounded-lg p-6 shadow-sm hover:shadow-md transition-shadow border border-white/60`}
          >
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1">
                <p className="text-sm font-medium text-slate-600 mb-1">{metric.label}</p>
                <p className="text-3xl font-bold text-slate-900 mb-3">{metric.value}</p>
                <p className="text-xs text-slate-600 leading-relaxed">{metric.tooltip}</p>
              </div>
              <div className={`${metric.bgIcon} p-3 rounded-full flex-shrink-0`}>
                <Icon size={24} className={metric.iconColor} />
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
