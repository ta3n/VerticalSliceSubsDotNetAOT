import {type SubmitEvent, useState} from 'react';
import {decimalBody, request} from './api';
import {ErrorNotice, Modal} from './components';
import {useAction} from './hooks';

export function CreateSubscription({close, created}: { close: () => void; created: () => void }) {
    const action = useAction();
    const [startDate, setStartDate] = useState(() => {
        const now = new Date();
        return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    });

    function submit(event: SubmitEvent<HTMLFormElement>) {
        event.preventDefault();
        const data = new FormData(event.currentTarget);
        void action.run(async () => {
            const trial = String(data.get('trialEndsAt') || '');
            if (trial && trial <= startDate) throw new Error('Ngày kết thúc dùng thử phải sau ngày bắt đầu.');
            await request('/subscriptions', {
                method: 'POST', body: decimalBody({
                    serviceName: String(data.get('serviceName')).trim(),
                    startDate, trialEndsAt: trial || null,
                }, 'price', String(data.get('price')))
            });
            created();
            close();
        });
    }

    return <Modal title="Tạo đăng ký mới" close={close} busy={action.pending}>
        <p className="muted">Thêm dịch vụ và bắt đầu quản lý chu kỳ thanh toán.</p>
        <form onSubmit={submit}>
            <fieldset disabled={action.pending}>
                <label>Tên dịch vụ<input name="serviceName" required maxLength={100}
                                         placeholder="Ví dụ: Không gian làm việc Pro" autoFocus/></label>
                <label>Giá mỗi kỳ (USD)<input name="price" type="number" required min="0" step="any"
                                              placeholder="29.00"/></label>
                <div className="form-grid">
                    <label>Ngày bắt đầu<input name="startDate" type="date" required value={startDate}
                                              onChange={e => setStartDate(e.target.value)}/></label>
                    <label>Kết thúc dùng thử <small>(tùy chọn)</small><input name="trialEndsAt" type="date"
                                                                             min={startDate}/></label>
                </div>
                <p className="form-hint">Gia hạn theo tháng, thanh toán bằng USD. Danh mục gói giá được quản lý
                    riêng.</p>
            </fieldset>
            <ErrorNotice error={action.error}/>
            <div className="modal-actions">
                <button type="button" className="button secondary" onClick={close} disabled={action.pending}>Để sau
                </button>
                <button className="button primary"
                        disabled={action.pending}>{action.pending ? 'Đang tạo…' : 'Tạo đăng ký'}</button>
            </div>
        </form>
    </Modal>;
}

export function CreatePlan({close, created}: { close: () => void; created: () => void }) {
    const action = useAction();

    function submit(event: SubmitEvent<HTMLFormElement>) {
        event.preventDefault();
        const data = new FormData(event.currentTarget);
        void action.run(async () => {
            await request('/plans', {
                method: 'POST', body: decimalBody({
                    code: String(data.get('code')).trim(),
                    name: String(data.get('name')).trim(),
                    currency: String(data.get('currency')).toUpperCase(),
                    interval: data.get('interval'),
                    trialDays: Number(data.get('trialDays')),
                }, 'amount', String(data.get('amount')))
            });
            created();
            close();
        });
    }

    return <Modal title="Thêm gói giá" close={close} busy={action.pending}>
        <p className="muted">Xây dựng danh mục giá cho dịch vụ của bạn.</p>
        <form onSubmit={submit}>
            <fieldset disabled={action.pending}>
                <label>Mã gói<input name="code" required maxLength={64} placeholder="PRO-MONTHLY" autoFocus/></label>
                <label>Tên gói<input name="name" required maxLength={200} placeholder="Gói chuyên nghiệp"/></label>
                <div className="form-grid">
                    <label>Giá<input name="amount" type="number" required min="0" step="any"
                                     placeholder="29.00"/></label>
                    <label>Tiền tệ<input name="currency" required pattern="[A-Za-z]{3}" maxLength={3} defaultValue="USD"
                                         title="Mã tiền tệ gồm 3 chữ cái, ví dụ USD"/></label>
                    <label>Chu kỳ<select name="interval">
                        <option value="MONTH">Hàng tháng</option>
                        <option value="YEAR">Hàng năm</option>
                    </select></label>
                    <label>Số ngày dùng thử<input name="trialDays" type="number" required min="0" max="365" step="1"
                                                  defaultValue="0"/></label>
                </div>
            </fieldset>
            <ErrorNotice error={action.error}/>
            <div className="modal-actions">
                <button type="button" className="button secondary" onClick={close} disabled={action.pending}>Để sau
                </button>
                <button className="button primary"
                        disabled={action.pending}>{action.pending ? 'Đang tạo…' : 'Thêm gói giá'}</button>
            </div>
        </form>
    </Modal>;
}
