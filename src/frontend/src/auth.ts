import Keycloak from 'keycloak-js';

export interface AuthConfig {
    url: string;
    realm: string;
    clientId: string;
}

/** Keycloak settings from the Vite env; without VITE_KEYCLOAK_URL authentication is off (no login, no header). */
export function authConfig(): AuthConfig | undefined {
    const url = import.meta.env.VITE_KEYCLOAK_URL as string | undefined;
    if (!url) return undefined;
    return {
        url,
        realm: (import.meta.env.VITE_KEYCLOAK_REALM as string | undefined) || 'subscriptions',
        clientId: (import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string | undefined) || 'frontend',
    };
}

interface Session {
    keycloak: Keycloak;
    username: string;
    roles: string[];
}

let session: Session | undefined;

/** Signs in (Authorization Code + PKCE) and resolves once the user is authenticated. A no-op when disabled. */
export async function initAuth(config = authConfig()): Promise<void> {
    if (!config) {
        session = undefined;
        return;
    }
    const keycloak = new Keycloak(config);
    const authenticated = await keycloak.init({onLoad: 'login-required', pkceMethod: 'S256', checkLoginIframe: false});
    if (!authenticated) {
        await keycloak.login();
        return;
    }
    const claims = (keycloak.tokenParsed ?? {}) as { preferred_username?: string; name?: string; roles?: string[] };
    session = {keycloak, username: claims.preferred_username || claims.name || '—', roles: claims.roles ?? []};
}

export function resetAuth() {
    session = undefined;
}

export const auth = {
    get enabled() {
        return session !== undefined;
    },
    get username() {
        return session?.username;
    },
    get roles() {
        return session?.roles ?? [];
    },
    /** Write actions need role "admin"; "viewer" is read-only. Everything is allowed when authentication is off. */
    get canWrite() {
        return !session || session.roles.includes('admin');
    },
    logout() {
        void session?.keycloak.logout({redirectUri: window.location.origin});
    },
};

/** A fresh access token (refreshed when it expires within 30 s), or undefined when authentication is off. */
export async function accessToken(): Promise<string | undefined> {
    if (!session) return undefined;
    try {
        await session.keycloak.updateToken(30);
    } catch {
        await session.keycloak.login();
    }
    return session.keycloak.token;
}
