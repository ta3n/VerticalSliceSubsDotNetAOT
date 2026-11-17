import {useEffect, useState} from 'react';
import {Icon} from './components';
import {Invoices} from './Invoices';
import {Plans} from './Plans';
import {SubscriptionDetail} from './SubscriptionDetail';
import {Subscriptions} from './Subscriptions';

const navigation = [
    {id: 'subscriptions', label: 'Đăng ký dịch vụ', icon: 'subscriptions'},
    {id: 'plans', label: 'Gói giá', icon: 'plans'},
    {id: 'invoices', label: 'Hóa đơn', icon: 'invoices'},
] as const;

export function App() {
    const [path, setPath] = useState(() => window.location.hash.slice(1) || 'subscriptions');
    useEffect(() => {
        const change = () => setPath(window.location.hash.slice(1) || 'subscriptions');
        window.addEventListener('hashchange', change);
        return () => window.removeEventListener('hashchange', change);
    }, []);
    const [section, id] = path.split('/');
    const current = navigation.find(item => item.id === section);
    useEffect(() => {
        document.title = `${current?.label || 'Không tìm thấy'} · Subs`;
    }, [current]);
    useEffect(() => {
        document.getElementById('main-content')?.focus();
    }, [path]);

    return <div className="app-shell">
        <a className="skip-link" href="#main-content" onClick={event => {
            event.preventDefault();
            document.getElementById('main-content')?.focus();
        }}>Đến nội dung chính</a>
        <aside className="sidebar">
            <a href="#subscriptions" className="brand" aria-label="Subs — Trang chủ"><span
                className="brand-mark">s<span>↗</span></span><span>subs<span
                className="brand-period">.</span></span></a>
            <div className="workspace"><span className="workspace-icon">S</span>
                <div><strong>Subscription workspace</strong><small>Quản trị dịch vụ</small></div>
            </div>
            <p className="nav-label">KHÔNG GIAN LÀM VIỆC</p>
            <nav aria-label="Điều hướng chính">{navigation.map(item => <a key={item.id} href={`#${item.id}`}
                                                                          className={section === item.id ? 'nav-item active' : 'nav-item'}
                                                                          aria-current={section === item.id ? 'page' : undefined}><Icon
                name={item.icon}/>{item.label}{section === item.id && <span className="nav-indicator"/>}</a>)}</nav>
            <div className="sidebar-bottom">
                <div className="sidebar-note"><Icon name="subscriptions"/><strong>Quản lý nhẹ nhàng hơn.</strong><p>Từ
                    đăng ký đến thanh toán,<br/>mọi thứ trong một không gian.</p></div>
                <div className="profile"><span className="profile-avatar">AD</span>
                    <div><strong>Không gian quản trị</strong><small>Subscription Management</small></div>
                </div>
            </div>
        </aside>
        <div className="main-shell">
            <div className="topbar">
                <div
                    className="breadcrumb">Workspace<span>/</span><strong>{current?.label || 'Không tìm thấy'}</strong>{id && <>
                    <span>/</span><span>Chi tiết</span></>}</div>
                <span className="topbar-label">SUBSCRIPTION MANAGEMENT</span></div>
            <main id="main-content" tabIndex={-1} key={`${section}/${id || ''}`}>
                {section === 'subscriptions' ? id ? <SubscriptionDetail id={id}/> :
                    <Subscriptions/> : section === 'plans' ? <Plans/> : section === 'invoices' ? <Invoices/> :
                    <div className="empty"><h1>Không tìm thấy trang</h1><a href="#subscriptions">Về danh sách đăng
                        ký</a></div>}
            </main>
            <footer className="app-footer">
                <span>Subs · Subscription workspace</span><span>Đơn giản hóa từng chu kỳ.</span></footer>
        </div>
    </div>;
}
