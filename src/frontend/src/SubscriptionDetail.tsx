import {useCallback, useState} from 'react';
import {api, type Invoice, request, type Subscription} from './api';
import {date, ErrorNotice, Icon, Loading, money, PageHeading, Status} from './components';
import {InvoiceTable, RenewalProgress} from './Invoices';
import {useAction, useResource} from './hooks';

export function SubscriptionDetail({id}: { id: string }) {
    const action = useAction();
    const [renewal, setRenewal] = useState<string>();
    const result = useResource(useCallback(async (signal: AbortSignal) => {
        const [subscription, invoices] = await Promise.all([api.subscription(id, signal), api.invoices(id, signal)]);
        return {subscription, invoices: invoices.items};
    }, [id]));
    const subscription = result.data?.subscription;

    function update(operation: 'pause' | 'resume' | 'cancel' | 'renew' | 'renewals') {
        const confirmations: Partial<Record<typeof operation, string>> = {
            cancel: 'Hủy đăng ký này? Bạn sẽ không thể kích hoạt lại. Hệ thống có thể xử lý hoàn tiền cho các thanh toán liên quan.',
            renew: 'Gia hạn thêm một tháng và tạo hóa đơn để xử lý thanh toán thủ công?',
            renewals: 'Gia hạn thêm một tháng và yêu cầu hệ thống tự động thu tiền (bao gồm thuế)?',
        };
        const confirmation = confirmations[operation];
        if (confirmation && !window.confirm(confirmation)) return;
        void action.run(async () => {
            const response = await request<Subscription | { subscription: Subscription; invoice: Invoice }>(
                `/subscriptions/${id}${operation === 'cancel' ? '' : `/${operation}`}`,
                {method: operation === 'cancel' ? 'DELETE' : 'POST'},
            );
            if (operation === 'renewals' && 'invoice' in response) setRenewal(response.invoice.id);
            result.refresh();
        }, 'Đã cập nhật đăng ký.');
    }

    return <>
        <a className="back-link" href="#subscriptions">← Danh sách đăng ký</a>
        <PageHeading eyebrow="CHI TIẾT ĐĂNG KÝ" title={subscription?.serviceName || 'Thông tin đăng ký'}
                     description="Quản lý trạng thái, chu kỳ và lịch sử hóa đơn.">
            <button className="button secondary" onClick={result.refresh} disabled={action.pending || result.loading}>
                <Icon name="refresh"/>Làm mới
            </button>
        </PageHeading>
        <ErrorNotice error={action.error}/>
        {action.message && <p className="notice success" role="status">{action.message}</p>}
        {result.loading ? <Loading/> : result.error ?
            <ErrorNotice error={result.error} retry={result.refresh}/> : subscription && <>
            <section className="panel detail-panel" aria-labelledby="subscription-info">
                <div className="section-heading"><h2 id="subscription-info">Thông tin dịch vụ</h2><Status
                    value={subscription.status}/></div>
                <dl className="detail-grid">
                    <div>
                        <dt>Giá hàng tháng</dt>
                        <dd className="price">{money(subscription.price)}</dd>
                    </div>
                    <div>
                        <dt>Ngày bắt đầu</dt>
                        <dd>{date(subscription.startDate)}</dd>
                    </div>
                    <div>
                        <dt>Kết thúc dùng thử</dt>
                        <dd>{date(subscription.trialEndsAt)}</dd>
                    </div>
                    <div className="full-width">
                        <dt>ID đăng ký</dt>
                        <dd className="mono">{subscription.id}</dd>
                    </div>
                </dl>
                <div className="detail-actions">
                    {subscription.status === 1 && <>
                        <button className="button secondary" disabled={action.pending}
                                onClick={() => update('pause')}>Tạm dừng
                        </button>
                        <button className="button secondary" disabled={action.pending}
                                onClick={() => update('renew')}>Gia hạn thủ công
                        </button>
                        <button className="button primary" disabled={action.pending}
                                onClick={() => update('renewals')}>Gia hạn tự động<Icon name="arrow"/></button>
                    </>}
                    {subscription.status === 3 && <button className="button primary" disabled={action.pending}
                                                          onClick={() => update('resume')}>Tiếp tục dịch vụ</button>}
                    {subscription.status !== 2 && <button className="button danger-button" disabled={action.pending}
                                                          onClick={() => update('cancel')}>Hủy đăng ký</button>}
                    {subscription.status === 2 &&
                        <p className="muted">Đăng ký đã hủy. Bạn có thể tạo đăng ký mới từ danh sách.</p>}
                    {action.pending && <span role="status">Đang xử lý…</span>}
                </div>
            </section>
            {renewal && <RenewalProgress id={renewal} key={renewal}/>}
            <section className="panel" aria-labelledby="invoice-title">
                <div className="panel-toolbar">
                    <div><h2 id="invoice-title">Lịch sử hóa đơn</h2><p
                        className="muted">{result.data?.invoices.length} hóa đơn · mới nhất trước</p></div>
                    <Icon name="invoices"/></div>
                <InvoiceTable items={result.data?.invoices || []} refresh={result.refresh} locked={action.pending}/>
            </section>
            <p className="info-note">Gia hạn thủ công tạo hóa đơn để đánh dấu thanh toán riêng. Gia hạn tự động bao gồm
                thuế và yêu cầu dịch vụ thanh toán được cấu hình. Sau khi xử lý, chọn “Làm mới” để cập nhật hóa đơn và
                trạng thái.</p>
        </>}
    </>;
}
