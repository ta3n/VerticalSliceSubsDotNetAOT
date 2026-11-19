import react from '@vitejs/plugin-react';
import {defineConfig, loadEnv} from 'vite';

export default defineConfig(({mode}) => {
    const env = loadEnv(mode, process.cwd(), '');
    const proxy = {
        '/api': {
            target: env.API_PROXY_TARGET || 'http://localhost:5127',
            changeOrigin: true,
            rewrite: (path: string) => path.replace(/^\/api/, ''),
        },
        // The Reporting service keeps its own path (/reporting/revenue): no rewrite.
        '/reporting': {
            target: env.REPORTING_PROXY_TARGET || 'http://localhost:5131',
            changeOrigin: true,
        },
    };
    return {plugins: [react()], server: {proxy, strictPort: true}, preview: {proxy}};
});
