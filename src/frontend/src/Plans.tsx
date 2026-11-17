import {useCallback, useState} from 'react';
import {api} from './api';
import {Empty, ErrorNotice, Icon, Loading, money, PageHeading} from './components';
import {CreatePlan} from './forms';
import {useResource} from './hooks';

export function Plans() {
    const [create, setCreate] = useState(false);
    const result = useResource(useCallback((signal: AbortSignal) => api.plans(signal), []));
    return <>
        <PageHeading eyebrow="DANH MỤC DỊCH VỤ" title="Gói giá"
                     description="Một nơi cho các mức giá và chu kỳ dịch vụ của bạn.">
            <button className="button primary" onClick={() => setCreate(true)}><Icon name="plus"/>Thêm gói giá</button>
        </PageHeading>
        <section className="panel" aria-labelledby="plans-title">
            <div className="panel-toolbar">
                <div><h2 id="plans-title">Danh mục gói đang hoạt động</h2><p
                    className="muted">{result.data ? `${result.data.items.length} gói trong danh mục` : 'Dữ liệu từ hệ thống'}</p>
                </div>
                <button className="icon-button" aria-label="Làm mới gói giá" onClick={result.refresh}
                        disabled={result.loading}><Icon name="refresh"/></button>
            </div>
            {result.loading ? <Loading/> : result.error ?
                <ErrorNotice error={result.error} retry={result.refresh}/> : !result.data?.items.length ?
                    <Empty title="Danh mục đang trống">Thêm gói đầu tiên để lưu mức giá và thời gian dùng thử.</Empty> :
                    <div className="table-scroll" role="region" aria-label="Bảng gói giá" tabIndex={0}>
                        <table>
                            <thead>
                            <tr>
                                <th>Gói dịch vụ</th>
                                <th>Mức giá</th>
                                <th>Chu kỳ</th>
                                <th>Dùng thử</th>
                            </tr>
                            </thead>
                            <tbody>
                            {result.data.items.map(plan => <tr key={plan.code}>
                                <td><strong>{plan.name}</strong><small className="mono">{plan.code}</small></td>
                                <td className="amount">{money(plan.amount, plan.currency)}</td>
                                <td>{plan.interval === 'MONTH' ? 'Hàng tháng' : 'Hàng năm'}</td>
                                <td>{plan.trialDays ? `${plan.trialDays} ngày` : 'Không dùng thử'}</td>
                            </tr>)}
                            </tbody>
                        </table>
                    </div>}
        </section>
        <p className="info-note">Gói giá là danh mục độc lập. Mỗi đăng ký hiện dùng tên dịch vụ và giá riêng, chưa liên
            kết với mã gói.</p>
        {create && <CreatePlan close={() => setCreate(false)} created={result.refresh}/>}
    </>;
}
