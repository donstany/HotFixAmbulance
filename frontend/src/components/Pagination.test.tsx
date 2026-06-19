import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Pagination, pageList } from './Pagination';

describe('pageList', () => {
  it('lists every page when there are few', () => {
    expect(pageList(1, 3)).toEqual([1, 2, 3]);
  });
  it('inserts ellipses around the current page in long ranges', () => {
    expect(pageList(6, 20)).toEqual([1, 'ellipsis', 5, 6, 7, 'ellipsis', 20]);
  });
});

describe('<Pagination />', () => {
  const base = {
    page: 1,
    pageSize: 25,
    totalItems: 213,
    totalPages: 9,
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
  };

  it('shows the range caption', () => {
    render(<Pagination {...base} />);
    expect(screen.getByText(/Showing 1.*25 of 213/)).toBeInTheDocument();
  });

  it('disables Prev on the first page', () => {
    render(<Pagination {...base} />);
    expect(screen.getByRole('button', { name: /prev/i })).toBeDisabled();
  });

  it('disables Next on the last page', () => {
    render(<Pagination {...base} page={9} />);
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });

  it('emits the target page when a number is clicked', async () => {
    const onPageChange = vi.fn();
    render(<Pagination {...base} onPageChange={onPageChange} />);
    await userEvent.click(screen.getByRole('button', { name: '3' }));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  it('emits a new page size when the selector changes', async () => {
    const onPageSizeChange = vi.fn();
    render(<Pagination {...base} onPageSizeChange={onPageSizeChange} />);
    await userEvent.selectOptions(screen.getByLabelText(/rows per page/i), '50');
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });
});
