import {render, screen} from '@testing-library/react';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';
import {api, ApiError, request} from './api';
import {auth, initAuth, resetAuth} from './auth';
import {Plans} from './Plans';
import {Subscriptions} from './Subscriptions';
import {SubscriptionDetail} from './SubscriptionDetail';
import {UserMenu} from './UserMenu';

const keycloak = vi.hoisted(() => ({
    instance: undefined as undefined | Record<string, ReturnType<typeof vi.fn> | unknown>,
    tokenParsed: {} as Record<string, unknown>,
}));

vi.mock('keycloak-js', () => ({
    default: class {
        token = 'access-token';
        init = vi.fn().mockResolvedValue(true);
        login = vi.fn().mockResolvedValue(undefined);
        logout = vi.fn().mockResolvedValue(undefined);
        updateToken = vi.fn().mockResolvedValue(true);

        constructor(public config: unknown) {
            // Shared with the tests so they can inspect the calls.
            keycloak.instance = this as never;
        }

        get tokenParsed() {
            return keycloak.tokenParsed;
        }
    },
}));

const config = {url: 'http://localhost:8081', realm: 'subscriptions', clientId: 'frontend'};
const signIn = (roles: string[]) => {
    keycloak.tokenParsed = {preferred_username: 'alice', roles};
    return initAuth(config);
};
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), {status});

beforeEach(() => vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(json({
    items: [],
    nextCursor: null
})))));
afterEach(() => resetAuth());

describe('Authentication disabled', () => {
    it('adds no header and allows writes', async () => {
        await initAuth(undefined);
        await request('/plans');

        expect(auth.enabled).toBe(false);
        expect(auth.canWrite).toBe(true);
        expect(vi.mocked(fetch).mock.calls[0][1]?.headers).not.toHaveProperty('Authorization');
        const {container} = render(<UserMenu/>);
        expect(container).toBeEmptyDOMElement();
    });
});

describe('Authentication enabled', () => {
    it('signs in with the authorization code flow and PKCE', async () => {
        await signIn(['admin']);

        expect(keycloak.instance!.config).toEqual(config);
        expect(keycloak.instance!.init).toHaveBeenCalledWith(expect.objectContaining({
            onLoad: 'login-required', pkceMethod: 'S256',
        }));
    });

    it('sends the bearer token on API and reporting requests, refreshing it first', async () => {
        await signIn(['admin']);
        await request('/plans');
        await api.revenue();

        const calls = vi.mocked(fetch).mock.calls;
        expect(calls[0][0]).toBe('/api/plans');
        expect(calls[1][0]).toBe('/reporting/revenue');
        for (const call of calls) expect(call[1]?.headers).toMatchObject({Authorization: 'Bearer access-token'});
        expect(keycloak.instance!.updateToken).toHaveBeenCalledTimes(2);
    });

    it('goes back to the login page when the token cannot be refreshed', async () => {
        await signIn(['admin']);
        vi.mocked(keycloak.instance!.updateToken as never as () => Promise<boolean>).mockRejectedValue(new Error('expired'));
        await request('/plans');

        expect(keycloak.instance!.login).toHaveBeenCalled();
    });

    it('explains 401 and 403 in Vietnamese', async () => {
        await signIn(['viewer']);
        vi.mocked(fetch).mockResolvedValueOnce(json({title: 'Unauthorized'}, 401))
            .mockResolvedValueOnce(json({title: 'Forbidden'}, 403));

        await expect(request('/plans')).rejects.toMatchObject({
            status: 401,
            message: expect.stringContaining('đăng nhập lại')
        });
        const forbidden = await request('/plans', {method: 'POST', body: '{}'}).catch(error => error);
        expect(forbidden).toBeInstanceOf(ApiError);
        expect(forbidden).toMatchObject({status: 403, message: expect.stringContaining('không có quyền')});
    });

    it('shows the user, the role and a sign-out button', async () => {
        await signIn(['viewer']);
        render(<UserMenu/>);

        expect(screen.getByText('alice')).toBeVisible();
        expect(screen.getByText('Chỉ xem')).toBeVisible();
        screen.getByRole('button', {name: 'Đăng xuất'}).click();
        expect(keycloak.instance!.logout).toHaveBeenCalled();
    });

    it('lets an admin create, and hides every write action from a viewer', async () => {
        await signIn(['admin']);
        const admin = render(<><Subscriptions/><Plans/></>);
        expect(await screen.findByRole('button', {name: 'Tạo đăng ký'})).toBeVisible();
        expect(screen.getByRole('button', {name: 'Thêm gói giá'})).toBeVisible();
        admin.unmount();

        await signIn(['viewer']);
        render(<><Subscriptions/><Plans/></>);
        await screen.findAllByText(/Chưa có đăng ký phù hợp|Danh mục đang trống/);
        expect(screen.queryByRole('button', {name: 'Tạo đăng ký'})).not.toBeInTheDocument();
        expect(screen.queryByRole('button', {name: 'Thêm gói giá'})).not.toBeInTheDocument();
    });

    it('hides the subscription actions from a viewer', async () => {
        await signIn(['viewer']);
        vi.spyOn(api, 'subscription').mockResolvedValue({
            id: '11111111-1111-1111-1111-111111111111', serviceName: 'Team workspace', price: 29, status: 1,
            startDate: '2026-11-01', trialEndsAt: null,
        });
        vi.spyOn(api, 'invoices').mockResolvedValue({items: []});
        render(<SubscriptionDetail id="11111111-1111-1111-1111-111111111111"/>);

        expect(await screen.findByText('Tài khoản của bạn chỉ có quyền xem.')).toBeVisible();
        for (const name of ['Tạm dừng', 'Gia hạn thủ công', 'Gia hạn tự động', 'Hủy đăng ký'])
            expect(screen.queryByRole('button', {name})).not.toBeInTheDocument();
    });
});
