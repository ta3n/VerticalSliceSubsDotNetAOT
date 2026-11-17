import {type ReactNode, useEffect, useId, useRef} from 'react';
import {ApiError} from './api';

export function Icon({name}: { name: 'subscriptions' | 'plans' | 'invoices' | 'plus' | 'arrow' | 'refresh' }) {
    const paths = {
        subscriptions: 'M4 5h16v14H4z M4 10h16 M8 15h4',
        plans: 'M4 4h7l9 9-7 7-9-9z M8 8h.01',
        invoices: 'M6 3h12v18l-3-2-3 2-3-2-3 2z M9 8h6 M9 12h6',
        plus: 'M12 5v14 M5 12h14',
        arrow: 'M5 12h14 M13 6l6 6-6 6',
        refresh: 'M20 7v5h-5 M4 17v-5h5 M6 7a7 7 0 0 1 12-1l2 6 M4 12l2 6a7 7 0 0 0 12-1',
    };
    return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"
                strokeLinejoin="round" aria-hidden="true">
        <path d={paths[name]}/>
    </svg>;
}

export function ErrorNotice({error, retry}: { error?: Error; retry?: () => void }) {
    if (!error) return null;
    return <div className="notice error" role="alert">
        <strong>Không thể hoàn tất yêu cầu</strong><p>{error.message}</p>
        {error instanceof ApiError && <>
            {error.errors && <ul>{Object.entries(error.errors).map(([field, messages]) => <li
                key={field}>{field}: {messages.join(', ')}</li>)}</ul>}
            {error.traceId && <small>Mã tra cứu: <code>{error.traceId}</code></small>}
        </>}
        {retry && <button className="button secondary" onClick={retry}>Thử lại</button>}
    </div>;
}

export function Loading() {
    return <div className="loading" role="status" aria-label="Đang tải dữ liệu">
        <div/>
        <div/>
        <div/>
        <span>Đang tải dữ liệu…</span></div>;
}

export function Empty({title, children}: { title: string; children?: ReactNode }) {
    return <div className="empty"><span className="empty-icon"><Icon name="subscriptions"/></span><h3>{title}</h3>
        <p>{children}</p></div>;
}

export function Status({value, invoice = false}: { value: number; invoice?: boolean }) {
    const labels: Record<number, string> = invoice
        ? {1: 'Chờ thanh toán', 2: 'Đã thanh toán', 3: 'Đã vô hiệu'}
        : {1: 'Đang hoạt động', 2: 'Đã hủy', 3: 'Tạm dừng'};
    const tone = invoice ? (value === 2 ? 'green' : value === 1 ? 'amber' : 'gray') : (value === 1 ? 'green' : value === 3 ? 'amber' : 'gray');
    return <span className={`status ${tone}`}><span aria-hidden="true"/>{labels[value] || `Trạng thái ${value}`}</span>;
}

export function Modal({title, close, busy, children}: {
    title: string;
    close: () => void;
    busy: boolean;
    children: ReactNode
}) {
    const ref = useRef<HTMLDialogElement>(null);
    const id = useId();
    useEffect(() => {
        const dialog = ref.current!;
        dialog.showModal();
        dialog.querySelector<HTMLInputElement>('input')?.focus();
        return () => dialog.close();
    }, []);
    return <dialog ref={ref} aria-labelledby={id} onCancel={event => {
        event.preventDefault();
        if (!busy) close();
    }}>
        <div className="modal-heading"><h2 id={id}>{title}</h2>
            <button className="icon-button" aria-label="Đóng" onClick={close} disabled={busy}>×</button>
        </div>
        {children}
    </dialog>;
}

export function PageHeading({eyebrow, title, description, children}: {
    eyebrow: string;
    title: string;
    description: string;
    children?: ReactNode
}) {
    return <header className="page-heading">
        <div><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p className="muted">{description}</p></div>
        <div className="heading-actions">{children}</div>
    </header>;
}

export function money(amount: number, currency = 'USD') {
    try {
        return new Intl.NumberFormat('vi-VN', {style: 'currency', currency, maximumFractionDigits: 4}).format(amount);
    } catch {
        return `${new Intl.NumberFormat('vi-VN', {maximumFractionDigits: 4}).format(amount)} ${currency}`;
    }
}

export function date(value: string | null) {
    if (!value) return '—';
    return new Intl.DateTimeFormat('vi-VN').format(new Date(value.length === 10 ? `${value}T00:00:00` : value));
}
