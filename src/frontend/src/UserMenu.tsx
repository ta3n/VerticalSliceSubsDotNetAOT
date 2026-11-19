import {auth} from './auth';

/** Signed-in user, role and sign-out; renders nothing when authentication is off. */
export function UserMenu() {
    if (!auth.enabled) return null;
    const role = auth.roles.includes('admin') ? 'Quản trị' : auth.roles.includes('viewer') ? 'Chỉ xem' : 'Không có vai trò';
    return <div className="user-menu">
        <span className="profile-avatar" aria-hidden="true">{auth.username!.slice(0, 2).toUpperCase()}</span>
        <div><strong>{auth.username}</strong><span
            className={`status ${auth.canWrite ? 'green' : 'gray'}`}>{role}</span>
        </div>
        <button className="button secondary small" onClick={() => auth.logout()}>Đăng xuất</button>
    </div>;
}
