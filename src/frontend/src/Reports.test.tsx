import {render, screen, waitFor, within} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {describe, expect, it, vi} from 'vitest';
import type {RevenueSummary} from './api';
import {Reports} from './Reports';

const summary: RevenueSummary = {
  currencies: [{currency: 'USD', invoicesIssued: 4, invoiced: 200, collected: 150, refunded: 25}],
  paymentsSucceeded: 3,
  paymentsFailed: 1,
  paymentSuccessRate: 0.75,
  subscriptionsCanceled: 2,
};

const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), {status});

describe('Reports', () => {
  it('shows the KPIs and the revenue per currency', async () => {
    const fetch = vi.fn().mockResolvedValue(json(summary));
    vi.stubGlobal('fetch', fetch);
    render(<Reports/>);

    const kpis = await screen.findByLabelText('Chỉ số thanh toán');
    expect(within(kpis).getByText('75%')).toBeVisible();
    expect(within(kpis).getByText('Thanh toán thất bại').parentElement).toHaveTextContent('1');
    expect(within(kpis).getByText('Đăng ký đã hủy').parentElement).toHaveTextContent('2');
    const row = within(screen.getByRole('table')).getByText('USD').closest('tr')!;
    expect(row).toHaveTextContent('75%'); // collected / invoiced
    expect(row).toHaveTextContent(/125/); // net = 150 - 25
    expect(fetch).toHaveBeenCalledWith('/reporting/revenue', expect.anything());
    expect(screen.getByText(/Kafka/)).toBeVisible();
  });

  it('shows an empty state when nothing has been reported yet', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({
      currencies: [], paymentsSucceeded: 0, paymentsFailed: 0, paymentSuccessRate: null, subscriptionsCanceled: 0,
    })));
    render(<Reports/>);

    expect(await screen.findByText('Chưa có dữ liệu')).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows the error and can retry', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({title: 'boom'}, 500)).mockResolvedValue(json(summary));
    vi.stubGlobal('fetch', fetch);
    render(<Reports/>);

    expect(await screen.findByRole('alert')).toHaveTextContent('boom');
    await userEvent.click(screen.getByRole('button', {name: 'Thử lại'}));
    expect(await screen.findByRole('table')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('refreshes on demand and keeps the data on screen while doing so', async () => {
    let release!: (response: Response) => void;
    const fetch = vi.fn().mockResolvedValueOnce(json(summary)).mockReturnValueOnce(new Promise<Response>(done => {
      release = done;
    }));
    vi.stubGlobal('fetch', fetch);
    render(<Reports/>);
    await screen.findByRole('table');

    await userEvent.click(screen.getByRole('button', {name: 'Làm mới'}));
    expect(screen.getByRole('table')).toBeVisible(); // not replaced by a spinner
    release(json({...summary, paymentsSucceeded: 9}));
    await waitFor(() => expect(screen.getByLabelText('Chỉ số thanh toán')).toHaveTextContent('9'));
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('polls every 10 seconds only while auto refresh is on', async () => {
    vi.useFakeTimers({shouldAdvanceTime: true});
    try {
      const fetch = vi.fn().mockImplementation(() => Promise.resolve(json(summary)));
      vi.stubGlobal('fetch', fetch);
      render(<Reports/>);
      await screen.findByRole('table');
      expect(fetch).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(10_000);
      expect(fetch).toHaveBeenCalledTimes(1); // off by default

      await userEvent.click(screen.getByLabelText(/Tự động làm mới/));
      await vi.advanceTimersByTimeAsync(10_000);
      await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    } finally {
      vi.useRealTimers();
    }
  });
});
