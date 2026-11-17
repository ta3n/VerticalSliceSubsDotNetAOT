import {afterEach, describe, expect, it, vi} from 'vitest';
import {api, ApiError, decimalBody, request} from './api';

afterEach(() => vi.unstubAllGlobals());
describe('API transport', () => {
    it('preserves problem validation errors and trace IDs', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
            detail: 'Invalid price', errors: {price: ['must be greater than 0']}, traceId: 'trace-123',
        }), {status: 400})));
        await expect(request('/subscriptions')).rejects.toMatchObject({
            message: 'Invalid price', status: 400, errors: {price: ['must be greater than 0']}, traceId: 'trace-123',
        });
    });
    it('handles a successful 204 cancellation', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, {status: 204})));
        await expect(request('/subscriptions/id', {method: 'DELETE'})).resolves.toBeUndefined();
    });
    it('handles proxy errors and network failures without leaking response HTML', async () => {
        const fetch = vi.fn().mockResolvedValueOnce(new Response('<html>Error</html>', {status: 502})).mockRejectedValueOnce(new TypeError('Failed to fetch'));
        vi.stubGlobal('fetch', fetch);
        await expect(request('/plans')).rejects.toBeInstanceOf(ApiError);
        await expect(request('/plans')).rejects.toThrow('Không kết nối được máy chủ');
    });
    it('sends exact status and opaque cursor to the backend', async () => {
        const fetch = vi.fn().mockResolvedValue(new Response('{"items":[],"nextCursor":null}'));
        vi.stubGlobal('fetch', fetch);
        await api.search('PAUSED', 'opaque+/=');
        expect(fetch).toHaveBeenCalledWith('/api/subscriptions/search', expect.objectContaining({
            method: 'POST', body: JSON.stringify({status: 'PAUSED', limit: 20, cursor: 'opaque+/='}),
        }));
    });
    it('preserves decimal input and rejects anything that could change the JSON structure', () => {
        expect(decimalBody({name: 'Test "plan"'}, 'amount', '12345678901234567890.1234'))
            .toBe(String.raw`{"name":"Test \"plan\"","amount":12345678901234567890.1234}`);
        expect(() => decimalBody({}, 'price', '1,"admin":true')).toThrow();
        expect(() => decimalBody({}, 'price', 'NaN')).toThrow();
        expect(JSON.parse(decimalBody({}, 'amount', '0'))).toEqual({amount: 0});
    });
});
