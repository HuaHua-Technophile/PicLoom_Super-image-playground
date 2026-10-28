import { useCallback, useEffect, useState, type Dispatch, type SetStateAction } from 'react';
import { toErrorCode } from '../api/client';

export interface ApiListState<T> {
  items: T[] | null;
  loading: boolean;
  errorCode: string | null;
  reload: () => void;
  setItems: Dispatch<SetStateAction<T[] | null>>;
}

/**
 * Loads a collection once and exposes explicit loading / error / empty states.
 * `fetcher` must be stable (module-level or useCallback) to avoid refetch loops.
 */
export function useApiList<T>(fetcher: (signal: AbortSignal) => Promise<T[]>): ApiListState<T> {
  const [items, setItems] = useState<T[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const reload = useCallback(() => {
    setReloadKey((key) => key + 1);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setLoading(true);
    setErrorCode(null);

    fetcher(controller.signal)
      .then((list) => {
        if (active) {
          setItems(Array.isArray(list) ? list : []);
        }
      })
      .catch((error: unknown) => {
        if (!active || controller.signal.aborted) {
          return;
        }
        setErrorCode(toErrorCode(error));
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [fetcher, reloadKey]);

  return { items, loading, errorCode, reload, setItems };
}
