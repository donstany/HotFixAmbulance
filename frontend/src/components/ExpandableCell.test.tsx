import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ExpandableCell } from './ExpandableCell';

/** Temporarily fakes layout metrics so jsdom (which has no layout) can exercise overflow detection. */
function withClampedLayout(scroll: number, client: number, fn: () => void) {
  const sh = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollHeight');
  const ch = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientHeight');
  Object.defineProperty(HTMLElement.prototype, 'scrollHeight', { configurable: true, get: () => scroll });
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => client });
  try {
    fn();
  } finally {
    if (sh) Object.defineProperty(HTMLElement.prototype, 'scrollHeight', sh);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollHeight;
    if (ch) Object.defineProperty(HTMLElement.prototype, 'clientHeight', ch);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientHeight;
  }
}

describe('<ExpandableCell />', () => {
  it('renders an em-dash for an empty value', () => {
    render(<ExpandableCell value={null} title="Suggestion for Error" />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows the full text and no expand control for short content', () => {
    render(<ExpandableCell value="short text" title="Suggestion for Error" dataTestId="suggestion" />);
    expect(screen.getByTestId('suggestion')).toHaveTextContent('short text');
    expect(screen.queryByRole('button', { name: /show full/i })).not.toBeInTheDocument();
  });

  it('shows a "Show more" control for long content and opens the full text in a dialog', async () => {
    const long = 'A'.repeat(400);
    render(<ExpandableCell value={long} title="How to fix" dataTestId="howtofix" />);
    const more = screen.getByRole('button', { name: /show full how to fix/i });
    await userEvent.click(more);
    const dialog = screen.getByRole('dialog', { name: /how to fix/i });
    expect(dialog).toHaveTextContent(long);
  });

  it('closes the dialog with the Escape key', async () => {
    render(<ExpandableCell value={'B'.repeat(400)} title="Suggestion for Error" />);
    await userEvent.click(screen.getByRole('button', { name: /show full/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows the expand control when the cell is visually clamped, even for short text', () => {
    withClampedLayout(120, 48, () => {
      render(<ExpandableCell value={'one short line that the layout reports as clamped'} title="Message" />);
      expect(screen.getByRole('button', { name: /show full message/i })).toBeInTheDocument();
    });
  });
});
