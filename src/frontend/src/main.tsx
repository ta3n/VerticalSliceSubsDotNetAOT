import {StrictMode} from 'react';
import {createRoot} from 'react-dom/client';
import {App} from './App';
import {initAuth} from './auth';
import './styles.css';

// Signs in first when Keycloak is configured (VITE_KEYCLOAK_URL); otherwise this resolves at once.
initAuth().then(
    () => createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>),
    () => {
        document.getElementById('root')!.textContent = 'Không thể kết nối dịch vụ đăng nhập. Vui lòng thử lại sau.';
    },
);
