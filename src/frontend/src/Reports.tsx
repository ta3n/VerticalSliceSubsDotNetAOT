import {useEffect, useState} from 'react';
import {api, type CurrencyRevenue, type RevenueSummary} from './api';
import {Empty, ErrorNotice, Icon, Loading, money, PageHeading, percent} from './components';

const refreshEvery = 10_000;

/** Revenue read model of the Reporting service. Keeps the last data on screen while it refreshes. */
export function Reports() {
    const [state, setState] = useState<{ data?: RevenueSummary; error?: Error; loading: boolean }>({loading: true});
    const [revision, setRevision] = useState(0);
    const [auto, setAuto] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        setState(previous => ({data: previous.data, loading: true}));
        api.revenue(controller.signal).then(
            data => {
                if (!controller.signal.aborted) setState({data, loading: false});
            },
            error => {
                if (!controller.signal.aborted) setState(previous => ({data: previous.data, error, loading: false}));
            },
        );
        return () => controller.abort();
    }, [revision]);

    useEffect(() => {
        if (!auto) return;
        const timer = setInterval(() => setRevision(value => value + 1), refreshEvery);
        return () => clearInterval(timer);
    }, [auto]);

    const refresh = () => setRevision(value => value + 1);
    const {data} = state;
    const empty = data && !data.currencies.length && !data.paymentsSucceeded && !data.paymentsFailed && !data.subscriptionsCanceled;

    return <>
        <PageHeading eyebrow="PHÂN TÍCH DOANH THU" title="Báo cáo"
                     description="Doanh thu, thu tiền và hoàn tiền tổng hợp từ luồng sự kiện.">
            <label className="toggle"><input type="checkbox" checked={auto}
                                             onChange={event => setAuto(event.target.checked)}/>Tự động làm mới (10
                giây)</label>
            <button className="button secondary" onClick={refresh} disabled={state.loading}><Icon name="refresh"/>Làm
                mới
            </button>
        </PageHeading>
        <ErrorNotice error={state.error} retry={refresh}/>
        {!data ? (state.loading ? <Loading/> : null) : empty ?
            <section className="panel"><Empty title="Chưa có dữ liệu">Báo cáo sẽ xuất hiện khi hệ thống phát sinh hóa
                đơn
                và thanh toán.</Empty></section> : <>
                <div className="summary four" aria-label="Chỉ số thanh toán">
                    <div><span>Thanh toán thành công</span><strong>{data.paymentsSucceeded}</strong></div>
                    <div><span>Thanh toán thất bại</span><strong>{data.paymentsFailed}</strong></div>
                    <div>
                        <span>Tỷ lệ thành công</span><strong>{data.paymentSuccessRate === null ? '—' : percent(data.paymentSuccessRate)}</strong>
                    </div>
                    <div><span>Đăng ký đã hủy</span><strong>{data.subscriptionsCanceled}</strong></div>
                </div>
                <section className="panel" aria-labelledby="revenue-title">
                    <div className="panel-toolbar">
                        <div><h2 id="revenue-title">Doanh thu theo tiền tệ</h2><p className="muted">Mỗi tiền tệ được
                            tính
                            riêng, không quy đổi.</p></div>
                    </div>
                    <div className="table-scroll" role="region" aria-label="Bảng doanh thu" tabIndex={0}>
                        <table>
                            <thead>
                            <tr>
                                <th>Tiền tệ</th>
                                <th>Hóa đơn</th>
                                <th>Đã xuất hóa đơn</th>
                                <th>Đã thu</th>
                                <th>Đã hoàn</th>
                                <th>Tỷ lệ thu</th>
                                <th>Thực thu</th>
                            </tr>
                            </thead>
                            <tbody>{data.currencies.map(row => <RevenueRow key={row.currency} row={row}/>)}</tbody>
                        </table>
                    </div>
                </section>
            </>}
        <p className="info-note">Dữ liệu được tổng hợp từ luồng sự kiện Kafka nên có thể trễ vài giây so với hệ thống
            (nhất quán sau cùng).</p>
    </>;
}

function RevenueRow({row}: { row: CurrencyRevenue }) {
    return <tr>
        <td><strong>{row.currency}</strong></td>
        <td>{row.invoicesIssued}</td>
        <td className="amount">{money(row.invoiced, row.currency)}</td>
        <td className="amount">{money(row.collected, row.currency)}</td>
        <td className="amount">{money(row.refunded, row.currency)}</td>
        <td>{row.invoiced > 0 ? percent(row.collected / row.invoiced) : '—'}</td>
        <td className="amount">{money(row.collected - row.refunded, row.currency)}</td>
    </tr>;
}
