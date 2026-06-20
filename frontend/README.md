# React + Vite frontend

- Vite + React + TypeScript
- TanStack Table v8 — **13-column triage table** (ID + 10 raw facts + *Suggestion for Error* + *How to fix*), server-side pagination, column show/hide via Column Settings
- TanStack Query v5 for backend data
- Tailwind CSS
- Vitest + React Testing Library

## AI columns

- A violet **🤖 Qwen badge** (`QwenBadge`) renders on the *Suggestion for Error* and *How to fix* cells whenever a group's `analyzedBy === 'Llm'` — the visible proof the analysis came from the Qwen model.
- Long cells are clamped to three lines by `ExpandableCell`; when the text overflows, a **Show more** control (and the cell itself) opens the full content in a dialog with Copy and Esc-to-close.

The API base URL is same-origin (`/api`, proxied to `http://localhost:5283` in dev — see `vite.config.ts`) or overridable via `VITE_HFA_API`.
