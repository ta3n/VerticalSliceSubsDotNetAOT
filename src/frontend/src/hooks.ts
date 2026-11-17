import {useEffect, useRef, useState} from 'react';

export function useResource<T>(load: (signal: AbortSignal) => Promise<T>) {
    const [state, setState] = useState<{ data?: T; error?: Error; loading: boolean }>({loading: true});
    const [revision, setRevision] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        setState({loading: true});
        load(controller.signal).then(
            data => {
                if (!controller.signal.aborted) setState({data, loading: false});
            },
            error => {
                if (!controller.signal.aborted) setState({error, loading: false});
            },
        );
        return () => controller.abort();
    }, [load, revision]);
    return {...state, refresh: () => setRevision(value => value + 1)};
}

export function useAction() {
    const locked = useRef(false);
    const [pending, setPending] = useState(false);
    const [error, setError] = useState<Error>();
    const [message, setMessage] = useState('');

    async function run(action: () => Promise<void>, success = '') {
        if (locked.current) return;
        locked.current = true;
        setPending(true);
        setError(undefined);
        setMessage('');
        try {
            await action();
            setMessage(success);
        } catch (cause) {
            setError(cause instanceof Error ? cause : new Error('Thao tác thất bại.'));
        } finally {
            locked.current = false;
            setPending(false);
        }
    }

    return {pending, error, message, run};
}
