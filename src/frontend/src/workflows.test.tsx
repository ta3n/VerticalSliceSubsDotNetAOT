import {act, render, screen, waitFor, within} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {describe, expect, it, vi} from 'vitest';
import {api, type Subscription} from './api';
import {CreatePlan, CreateSubscription} from './forms';
import {InvoiceTable, RenewalProgress} from './Invoices';
import {SubscriptionDetail} from './SubscriptionDetail';
import {Subscriptions} from './Subscriptions';

const id = '11111111-1111-1111-1111-111111111111';
const subscription: Subscription = {
    id,
    serviceName: 'Team workspace',
    price: 29,
    status: 1,
    startDate: '2026-11-01',
    trialEndsAt: null
};
const invoice = {
    id: '22222222-2222-2222-2222-222222222222',
    subscriptionId: id,
    amount: 29,
    currency: 'USD',
    status: 1,
    statusLabel: 'OPEN',
    periodStart: '2026-12-01',
    periodEnd: '2027-01-01',
    issuedAt: '2026-11-17T00:00:00Z',
    paidAt: null
};

function respond(data: unknown, status = 200) {
    return new Response(status === 204 ? null : JSON.stringify(data), {status});
}

describe('Subscription workflows', () => {
    it('paginates with backend cursors and resets the cursor when filtering', async () => {
        const user = userEvent.setup();
        const search = vi.spyOn(api, 'search').mockResolvedValueOnce({items: [subscription], nextCursor: 'next'})
            .mockResolvedValueOnce({items: [{...subscription, serviceName: 'Second page'}], nextCursor: null})
            .mockResolvedValueOnce({items: [], nextCursor: null});
        render(<Subscriptions/>);
        expect(await screen.findByText('Team workspace')).toBeVisible();
        await user.click(screen.getByRole('button', {name: 'Sau →'}));
        expect(await screen.findByText('Second page')).toBeVisible();
        await user.selectOptions(screen.getByLabelText('Lọc trạng thái'), 'PAUSED');
        expect(await screen.findByText('Chưa có đăng ký phù hợp')).toBeVisible();
        expect(search).toHaveBeenLastCalledWith('PAUSED', null, expect.any(AbortSignal));
        expect(screen.getByRole('button', {name: '← Trước'})).toBeDisabled();
    });

    it('does not display a stale response after a filter change', async () => {
        let resolve!: (value: { items: Subscription[]; nextCursor: null }) => void;
        vi.spyOn(api, 'search').mockReturnValueOnce(new Promise(done => {
            resolve = done;
        }))
            .mockResolvedValue({
                items: [{...subscription, status: 3, serviceName: 'Paused service'}],
                nextCursor: null
            });
        render(<Subscriptions/>);
        await userEvent.selectOptions(screen.getByLabelText('Lọc trạng thái'), 'PAUSED');
        expect(await screen.findByText('Paused service')).toBeVisible();
        await act(async () => resolve({items: [subscription], nextCursor: null}));
        expect(screen.queryByText('Team workspace')).not.toBeInTheDocument();
    });

    it('creates subscriptions with date-only fields and an exact decimal price', async () => {
        const fetch = vi.fn().mockResolvedValue(respond(subscription, 201));
        vi.stubGlobal('fetch', fetch);
        const created = vi.fn();
        const close = vi.fn();
        const user = userEvent.setup();
        render(<CreateSubscription created={created} close={close}/>);
        await user.type(screen.getByLabelText('Tên dịch vụ'), 'New service');
        await user.type(screen.getByLabelText('Giá mỗi kỳ (USD)'), '29.1234');
        await user.click(screen.getByRole('button', {name: 'Tạo đăng ký'}));
        await waitFor(() => expect(created).toHaveBeenCalledOnce());
        const body = JSON.parse(fetch.mock.calls[0][1].body);
        expect(body).toMatchObject({serviceName: 'New service', price: 29.1234, trialEndsAt: null});
        expect(body.startDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
        expect(close).toHaveBeenCalledOnce();
    });

    it('rejects a trial ending on the start date before sending a request', async () => {
        const fetch = vi.fn();
        vi.stubGlobal('fetch', fetch);
        const user = userEvent.setup();
        render(<CreateSubscription created={vi.fn()} close={vi.fn()}/>);
        await user.type(screen.getByLabelText('Tên dịch vụ'), 'Trial');
        await user.type(screen.getByLabelText('Giá mỗi kỳ (USD)'), '10');
        const start = screen.getByLabelText('Ngày bắt đầu') as HTMLInputElement;
        await user.type(screen.getByLabelText(/Kết thúc dùng thử/), start.value);
        await user.click(screen.getByRole('button', {name: 'Tạo đăng ký'}));
        expect(await screen.findByRole('alert')).toHaveTextContent('phải sau ngày bắt đầu');
        expect(fetch).not.toHaveBeenCalled();
    });

    it('only offers resume and cancellation for paused subscriptions, and handles 204', async () => {
        vi.spyOn(api, 'subscription').mockResolvedValue({...subscription, status: 3});
        vi.spyOn(api, 'invoices').mockResolvedValue({items: []});
        const fetch = vi.fn().mockResolvedValue(respond(null, 204));
        vi.stubGlobal('fetch', fetch);
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        render(<SubscriptionDetail id={id}/>);
        const cancel = await screen.findByRole('button', {name: 'Hủy đăng ký'});
        expect(screen.getByRole('button', {name: 'Tiếp tục dịch vụ'})).toBeEnabled();
        expect(screen.queryByRole('button', {name: 'Tạm dừng'})).not.toBeInTheDocument();
        expect(screen.queryByRole('button', {name: 'Gia hạn tự động'})).not.toBeInTheDocument();
        await userEvent.click(cancel);
        expect(fetch).not.toHaveBeenCalled();
        confirm.mockReturnValue(true);
        await userEvent.click(cancel);
        await waitFor(() => expect(fetch).toHaveBeenCalledWith(`/api/subscriptions/${id}`, expect.objectContaining({method: 'DELETE'})));
        expect(await screen.findByText('Đã cập nhật đăng ký.')).toBeVisible();
    });

    it('shows accepted automated renewals and their progress', async () => {
        vi.spyOn(api, 'subscription').mockResolvedValue(subscription);
        vi.spyOn(api, 'invoices').mockResolvedValue({items: []});
        vi.spyOn(api, 'renewal').mockResolvedValue({
            invoiceId: invoice.id,
            paymentId: null,
            state: 'AwaitingPayment',
            failureReason: null,
            updatedAt: null
        });
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respond({subscription, invoice}, 202)));
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        render(<SubscriptionDetail id={id}/>);
        await userEvent.click(await screen.findByRole('button', {name: 'Gia hạn tự động'}));
        expect(await screen.findByText('AwaitingPayment')).toBeVisible();
    });
});

describe('Plans and invoices', () => {
    it('creates a yearly plan with uppercase currency and trial days', async () => {
        const fetch = vi.fn().mockResolvedValue(respond({}, 201));
        vi.stubGlobal('fetch', fetch);
        const created = vi.fn();
        const user = userEvent.setup();
        render(<CreatePlan close={vi.fn()} created={created}/>);
        await user.type(screen.getByLabelText('Mã gói'), 'PRO');
        await user.type(screen.getByLabelText('Tên gói'), 'Pro');
        await user.type(screen.getByLabelText('Giá', {exact: true}), '120');
        await user.selectOptions(screen.getByLabelText('Chu kỳ'), 'YEAR');
        await user.clear(screen.getByLabelText('Tiền tệ'));
        await user.type(screen.getByLabelText('Tiền tệ'), 'usd');
        await user.clear(screen.getByLabelText('Số ngày dùng thử'));
        await user.type(screen.getByLabelText('Số ngày dùng thử'), '14');
        await user.click(screen.getByRole('button', {name: 'Thêm gói giá'}));
        await waitFor(() => expect(created).toHaveBeenCalledOnce());
        expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
            code: 'PRO',
            name: 'Pro',
            amount: 120,
            currency: 'USD',
            interval: 'YEAR',
            trialDays: 14
        });
    });

    it('does not offer pay/void on a paid invoice', () => {
        render(<InvoiceTable items={[{...invoice, status: 2}]} refresh={vi.fn()}/>);
        expect(screen.queryByRole('button', {name: 'Đánh dấu đã trả'})).not.toBeInTheDocument();
        expect(screen.queryByRole('button', {name: 'Vô hiệu'})).not.toBeInTheDocument();
    });

    it('confirms invoice payment and refreshes only after success', async () => {
        const fetch = vi.fn().mockResolvedValue(respond({...invoice, status: 2}));
        vi.stubGlobal('fetch', fetch);
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const refresh = vi.fn();
        render(<InvoiceTable items={[invoice]} refresh={refresh}/>);
        await userEvent.click(screen.getByRole('button', {name: 'Đánh dấu đã trả'}));
        await waitFor(() => expect(refresh).toHaveBeenCalledOnce());
        expect(fetch).toHaveBeenCalledWith(`/api/invoices/${invoice.id}/pay`, expect.objectContaining({method: 'POST'}));
    });

    it('allows retrying a renewal status that is not yet available', async () => {
        const fetch = vi.fn().mockResolvedValueOnce(respond({detail: 'Not ready'}, 404))
            .mockResolvedValueOnce(respond({invoiceId: invoice.id, state: 'Completed'}));
        vi.stubGlobal('fetch', fetch);
        render(<RenewalProgress id={invoice.id}/>);
        expect(await screen.findByText(/Chưa có tiến trình gia hạn/)).toBeVisible();
        await userEvent.click(screen.getByRole('button', {name: 'Kiểm tra lại'}));
        expect(await screen.findByText('Completed')).toBeVisible();
    });

    it('displays validation failures without closing the form', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respond({
            detail: 'Plan exists',
            traceId: 'trace-1',
            errors: {code: ['already exists']}
        }, 409)));
        const close = vi.fn();
        const user = userEvent.setup();
        render(<CreatePlan close={close} created={vi.fn()}/>);
        await user.type(screen.getByLabelText('Mã gói'), 'PRO');
        await user.type(screen.getByLabelText('Tên gói'), 'Pro');
        await user.type(screen.getByLabelText('Giá', {exact: true}), '12');
        await user.click(screen.getByRole('button', {name: 'Thêm gói giá'}));
        const alert = await screen.findByRole('alert');
        expect(within(alert).getByText('code: already exists')).toBeVisible();
        expect(alert).toHaveTextContent('trace-1');
        expect(close).not.toHaveBeenCalled();
    });
});
