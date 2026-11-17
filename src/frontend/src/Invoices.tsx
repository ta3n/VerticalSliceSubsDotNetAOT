import {type SubmitEvent, useCallback, useState} from 'react';
import {api, ApiError, type Invoice, request} from './api';
import {date, Empty, ErrorNotice, Icon, Loading, money, PageHeading, Status} from './components';
import {useAction, useResource} from './hooks';

export function RenewalProgress({id}: { id: string }) {
    const result = useResource(useCallback((signal: AbortSignal) => api.renewal(id, signal), [id]));
    return <section className="renewal-panel" aria-label="Tiến trình gia hạn">
        <div className="section-heading"><h3>Tiến trình gia hạn tự động</h3>
            <button className="button secondary small" disabled={result.loading} onClick={result.refresh}>Kiểm tra lại
            </button>
        </div>
        <small className="mono">Hóa đơn: {id}</small>
        {result.loading ?
            <p role="status">Đang kiểm tra…</p> : result.error instanceof ApiError && result.error.status === 404 ?
                <p role="status">Chưa có tiến trình gia hạn. Với hóa đơn tự động vừa tạo, hệ thống có thể đang tiếp
                    nhận; hãy kiểm tra lại sau.</p> : result.error ? <ErrorNotice error={result.error}/> :
                    <><p>Trạng thái: <strong>{result.data?.state}</strong></p>{result.data?.failureReason &&
                        <p className="failure-reason">{result.data.failureReason}</p>}{result.data?.paymentId &&
                        <small className="mono">Payment: {result.data.paymentId}</small>}</>}
    </section>;
}

export function InvoiceTable({items, refresh, locked = false}: {
    items: Invoice[];
    refresh: () => void;
    locked?: boolean
}) {
    const action = useAction();
    const [progress, setProgress] = useState<string>();

    function update(invoice: Invoice, operation: 'pay' | 'void') {
        const message = operation === 'pay' ? 'Đánh dấu hóa đơn này đã thanh toán? Thao tác này không thu tiền qua cổng thanh toán.' : 'Vô hiệu hóa hóa đơn chưa thanh toán này?';
        if (!window.confirm(message)) return;
        void action.run(async () => {
            await request(`/invoices/${invoice.id}/${operation}`, {method: 'POST'});
            refresh();
        }, operation === 'pay' ? 'Đã đánh dấu hóa đơn đã thanh toán.' : 'Đã vô hiệu hóa đơn.');
    }

    return <>
        <ErrorNotice error={action.error}/>
        {action.message && <p className="notice success" role="status">{action.message}</p>}
        {!items.length ? <Empty title="Chưa có hóa đơn">Hóa đơn sẽ xuất hiện khi đăng ký được gia hạn.</Empty> :
            <div className="table-scroll" role="region" aria-label="Bảng hóa đơn" tabIndex={0}>
                <table>
                    <thead>
                    <tr>
                        <th>Hóa đơn</th>
                        <th>Kỳ thanh toán</th>
                        <th>Số tiền</th>
                        <th>Trạng thái</th>
                        <th>Thao tác</th>
                    </tr>
                    </thead>
                    <tbody>
                    {items.map(invoice => <tr key={invoice.id}>
                        <td><strong className="mono" title={invoice.id}>{invoice.id.slice(0, 8)}</strong><small>Phát
                            hành {date(invoice.issuedAt)}</small>{invoice.paidAt &&
                            <small>Thanh toán {date(invoice.paidAt)}</small>}</td>
                        <td>{date(invoice.periodStart)}<small>đến {date(invoice.periodEnd)}</small></td>
                        <td className="amount">{money(invoice.amount, invoice.currency)}</td>
                        <td><Status value={invoice.status} invoice/></td>
                        <td>
                            <div className="invoice-actions">{invoice.status === 1 && <>
                                <button className="text-button" onClick={() => update(invoice, 'pay')}
                                        disabled={action.pending || locked}>Đánh dấu đã trả
                                </button>
                                <button className="text-button danger" onClick={() => update(invoice, 'void')}
                                        disabled={action.pending || locked}>Vô hiệu
                                </button>
                            </>}
                                <button className="text-button" onClick={() => setProgress(invoice.id)}
                                        disabled={action.pending || locked}>Xem gia hạn
                                </button>
                            </div>
                        </td>
                    </tr>)}
                    </tbody>
                </table>
            </div>}
        {progress && <RenewalProgress key={progress} id={progress}/>}
    </>;
}

function InvoiceLookup({id, mode}: { id: string; mode: string }) {
    const result = useResource(useCallback((signal: AbortSignal) => mode === 'subscription'
        ? api.invoices(id, signal)
        : request<Invoice>(`/invoices/${encodeURIComponent(id)}`, {signal}).then(invoice => ({items: [invoice]})), [id, mode]));
    return <section className="panel">
        <div className="panel-toolbar">
            <div><h2>Kết quả tra cứu</h2><small className="mono">{id}</small></div>
            <button className="icon-button" aria-label="Làm mới hóa đơn" disabled={result.loading}
                    onClick={result.refresh}><Icon name="refresh"/></button>
        </div>
        {result.loading ? <Loading/> : result.error ?
            <ErrorNotice error={result.error} retry={result.refresh}/> : result.data &&
            <InvoiceTable items={result.data.items} refresh={result.refresh}/>}
    </section>;
}

export function Invoices() {
    const [lookup, setLookup] = useState<{ id: string; mode: string }>();

    function submit(event: SubmitEvent<HTMLFormElement>) {
        event.preventDefault();
        const data = new FormData(event.currentTarget);
        setLookup({id: String(data.get('id')).trim(), mode: String(data.get('mode'))});
    }

    return <>
        <PageHeading eyebrow="THANH TOÁN & ĐỐI SOÁT" title="Hóa đơn"
                     description="Tra cứu hóa đơn và theo dõi tình trạng thanh toán."/>
        <section className="panel lookup-panel"><h2>Tìm hóa đơn</h2><p className="muted">Tra cứu toàn bộ hóa đơn của một
            đăng ký hoặc một hóa đơn cụ thể.</p>
            <form className="lookup-form" onSubmit={submit}><label>Tra cứu theo<select name="mode">
                <option value="subscription">ID đăng ký</option>
                <option value="invoice">ID hóa đơn</option>
            </select></label>
                <label className="grow">Mã định danh<input name="id" required
                                                           pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
                                                           placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                                                           title="Nhập ID có định dạng UUID"/></label>
                <button className="button primary">Tra cứu<Icon name="arrow"/></button>
            </form>
        </section>
        {lookup ? <InvoiceLookup key={`${lookup.mode}/${lookup.id}`} {...lookup} /> :
            <div className="info-note">Bạn cũng có thể xem hóa đơn trực tiếp trong trang chi tiết đăng ký.</div>}
    </>;
}
