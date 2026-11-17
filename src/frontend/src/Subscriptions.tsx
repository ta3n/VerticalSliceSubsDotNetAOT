import {useCallback, useState} from 'react';
import {api} from './api';
import {date, Empty, ErrorNotice, Icon, Loading, money, PageHeading, Status} from './components';
import {CreateSubscription} from './forms';
import {useResource} from './hooks';

export function Subscriptions() {
    const [status, setStatus] = useState('');
    const [cursors, setCursors] = useState<(string | null)[]>([null]);
    const [create, setCreate] = useState(false);
    const cursor = cursors[cursors.length - 1];
    const load = useCallback((signal: AbortSignal) => api.search(status, cursor, signal), [status, cursor]);
    const result = useResource(load);
    const items = result.data?.items || [];

    return <>
        <PageHeading eyebrow="KHÔNG GIAN QUẢN LÝ" title="Đăng ký dịch vụ"
                     description="Theo dõi dịch vụ và quản lý từng chu kỳ thanh toán.">
            <button className="button primary" onClick={() => setCreate(true)}><Icon name="plus"/>Tạo đăng ký</button>
        </PageHeading>
        <div className="summary" aria-label="Thống kê trang hiện tại">
            <div>
                <span>Đăng ký trong trang</span><strong>{result.data ? items.length.toString().padStart(2, '0') : '—'}</strong><small>Trang {cursors.length} ·
                tối đa 20 mục</small></div>
            <div>
                <span>Đang hoạt động</span><strong>{result.data ? items.filter(item => item.status === 1).length.toString().padStart(2, '0') : '—'}</strong><small><i
                className="dot green-dot"/>Trong trang hiện tại</small></div>
            <div>
                <span>Đang tạm dừng</span><strong>{result.data ? items.filter(item => item.status === 3).length.toString().padStart(2, '0') : '—'}</strong><small><i
                className="dot amber-dot"/>Trong trang hiện tại</small></div>
        </div>
        <section className="panel" aria-labelledby="list-title">
            <div className="panel-toolbar">
                <div><h2 id="list-title">Danh sách đăng ký</h2><p className="muted">Chọn dịch vụ để xem chi tiết và hóa
                    đơn.</p></div>
                <div className="toolbar-controls"><label className="sr-only" htmlFor="status-filter">Lọc trạng
                    thái</label><select id="status-filter" value={status} onChange={e => {
                    setStatus(e.target.value);
                    setCursors([null]);
                }}>
                    <option value="">Tất cả trạng thái</option>
                    <option value="ACTIVE">Đang hoạt động</option>
                    <option value="PAUSED">Tạm dừng</option>
                    <option value="CANCELED">Đã hủy</option>
                </select>
                    <button className="icon-button" aria-label="Làm mới danh sách" onClick={result.refresh}
                            disabled={result.loading}><Icon name="refresh"/></button>
                </div>
            </div>
            {result.loading ? <Loading/> : result.error ?
                <ErrorNotice error={result.error} retry={result.refresh}/> : items.length === 0 ?
                    <Empty title="Chưa có đăng ký phù hợp">Tạo đăng ký mới hoặc chọn trạng thái khác để bắt
                        đầu.</Empty> :
                    <div className="table-scroll" role="region" aria-label="Bảng đăng ký" tabIndex={0}>
                        <table>
                            <thead>
                            <tr>
                                <th>Dịch vụ</th>
                                <th>Giá / tháng</th>
                                <th>Ngày bắt đầu</th>
                                <th>Trạng thái</th>
                                <th><span className="sr-only">Chi tiết</span></th>
                            </tr>
                            </thead>
                            <tbody>
                            {items.map(item => <tr key={item.id}>
                                <td><a className="service-link" href={`#subscriptions/${item.id}`}><span
                                    className="service-avatar"
                                    aria-hidden="true">{item.serviceName.slice(0, 2).toUpperCase()}</span><span><strong>{item.serviceName}</strong><small
                                    className="mono">{item.id.slice(0, 8)}</small></span></a></td>
                                <td className="amount">{money(item.price)}</td>
                                <td>{date(item.startDate)}</td>
                                <td><Status value={item.status}/></td>
                                <td><a className="table-link" href={`#subscriptions/${item.id}`}
                                       aria-label={`Chi tiết ${item.serviceName}`}><Icon name="arrow"/></a></td>
                            </tr>)}
                            </tbody>
                        </table>
                    </div>}
            <div className="pagination"><span>{items.length} đăng ký trong trang {cursors.length}</span>
                <div className="button-group">
                    <button className="button secondary small" disabled={cursors.length === 1 || result.loading}
                            onClick={() => setCursors(value => value.slice(0, -1))}>← Trước
                    </button>
                    <button className="button secondary small" disabled={!result.data?.nextCursor || result.loading}
                            onClick={() => setCursors(value => [...value, result.data!.nextCursor])}>Sau →
                    </button>
                </div>
            </div>
        </section>
        <div className="page-note"><Icon name="subscriptions"/><p>Mỗi đăng ký, một góc nhìn rõ ràng.<br/><span>Trạng thái và hóa đơn luôn lấy trực tiếp từ hệ thống.</span>
        </p></div>
        {create && <CreateSubscription close={() => setCreate(false)} created={() => {
            setStatus('');
            setCursors([null]);
            result.refresh();
        }}/>}
    </>;
}
