import {accessToken} from './auth';

export interface Subscription {
    id: string;
    serviceName: string;
    price: number;
    startDate: string;
    trialEndsAt: string | null;
    status: number;
}

export interface Plan {
    code: string;
    name: string;
    amount: number;
    currency: string;
    interval: 'MONTH' | 'YEAR';
    trialDays: number;
    active: boolean;
}

export interface Invoice {
    id: string;
    subscriptionId: string;
    amount: number;
    currency: string;
    periodStart: string;
    periodEnd: string;
    status: number;
    statusLabel: string;
    issuedAt: string;
    paidAt: string | null;
}

export interface Renewal {
    invoiceId: string;
    paymentId: string | null;
    state: string;
    failureReason: string | null;
    updatedAt: string | null;
}

export interface CurrencyRevenue {
    currency: string;
    invoicesIssued: number;
    invoiced: number;
    collected: number;
    refunded: number;
}

export interface RevenueSummary {
    currencies: CurrencyRevenue[];
    paymentsSucceeded: number;
    paymentsFailed: number;
    paymentSuccessRate: number | null;
    subscriptionsCanceled: number;
}

export class ApiError extends Error {
    constructor(message: string, public status: number, public errors?: Record<string, string[]>, public traceId?: string) {
        super(message);
    }
}

// Keep decimal input exact when sending money to .NET instead of rounding it through a JS number.
export function decimalBody(fields: Record<string, unknown>, field: 'price' | 'amount', value: string) {
    if (!/^(0|[1-9]\d*)(\.\d+)?$/.test(value)) throw new Error('Nhập số tiền dạng thập phân, ví dụ 29.95.');
    const json = JSON.stringify(fields);
    return `${json.slice(0, -1)}${json.length > 2 ? ',' : ''}${JSON.stringify(field)}:${value}}`;
}

// 401/403 mean the token is missing, expired or lacks the role: say so instead of the server's generic problem text.
const authMessages: Record<number, string> = {
    401: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
    403: 'Bạn không có quyền thực hiện thao tác này (tài khoản chỉ có quyền xem).',
};

/** The single fetch helper: adds the bearer token (when authentication is on) and maps failures to messages. */
export async function send<T>(url: string, init?: RequestInit): Promise<T> {
    let response: Response;
    try {
        const token = await accessToken();
        response = await fetch(url, {
            ...init,
            headers: {
                ...(init?.body ? {'Content-Type': 'application/json'} : {}),
                ...(token ? {Authorization: `Bearer ${token}`} : {}),
                ...init?.headers,
            },
        });
    } catch (error) {
        if (error instanceof Error && error.name === 'AbortError') throw error;
        throw new Error('Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.');
    }
    if (response.status === 204) return undefined as T;
    const body = await response.text();
    let data;
    try {
        data = body ? JSON.parse(body) : null;
    } catch { /* Proxy failures may return plain text. */
    }
    if (!response.ok) {
        throw new ApiError(authMessages[response.status] || data?.detail || data?.title || `Yêu cầu thất bại (HTTP ${response.status}).`,
            response.status, data?.errors, data?.traceId || response.headers.get('X-Trace-Id') || undefined);
    }
    if (data === undefined || data === null) throw new Error('Máy chủ trả về dữ liệu không hợp lệ.');
    return data as T;
}

export const request = <T>(path: string, init?: RequestInit) => send<T>(`/api${path}`, init);

export const api = {
    search: (status: string, cursor: string | null, signal?: AbortSignal) =>
        request<{ items: Subscription[]; nextCursor: string | null }>('/subscriptions/search', {
            method: 'POST', body: JSON.stringify({status: status || null, limit: 20, cursor}), signal,
        }),
    subscription: (id: string, signal?: AbortSignal) => request<Subscription>(`/subscriptions/${encodeURIComponent(id)}`, {signal}),
    plans: (signal?: AbortSignal) => request<{ items: Plan[] }>('/plans', {signal}),
    invoices: (id: string, signal?: AbortSignal) => request<{
        items: Invoice[]
    }>(`/subscriptions/${encodeURIComponent(id)}/invoices`, {signal}),
    renewal: (id: string, signal?: AbortSignal) => request<Renewal>(`/invoices/${encodeURIComponent(id)}/renewal`, {signal}),
    // The Reporting service, not the API: its own proxy entry (/reporting) in dev and in the gateway.
    revenue: (signal?: AbortSignal) => send<RevenueSummary>('/reporting/revenue', {signal}),
};
