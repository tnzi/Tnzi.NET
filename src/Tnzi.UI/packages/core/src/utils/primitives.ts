/**
 * @tnzi/core/utils/primitives
 *
 * Framework-agnostic state utilities.
 */

import type { AsyncState, PaginationState } from '../types/stores';
import {
  errorAsyncState,
  initialAsyncState,
  initialPaginationState,
  loadingAsyncState,
  successAsyncState,
} from '../types/stores';
import { createMemoryStorageAdapter } from '../adapters/storage';

type Listener<T> = (state: T) => void;

function createNotifier<T>() {
  const listeners = new Set<Listener<T>>();
  return {
    notify(state: T) {
      listeners.forEach((listener) => listener(state));
    },
    subscribe(listener: Listener<T>) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

export function createAsyncState<T, E = Error>() {
  let state: AsyncState<T, E> = initialAsyncState();
  const { notify, subscribe } = createNotifier<AsyncState<T, E>>();

  return {
    getState: () => state,
    subscribe,
    setLoading: (previousData: T | null = null) => {
      state = loadingAsyncState(previousData);
      notify(state);
    },
    setSuccess: (data: T) => {
      state = successAsyncState(data);
      notify(state);
    },
    setError: (error: E, previousData: T | null = null) => {
      state = errorAsyncState(error, previousData);
      notify(state);
    },
    reset: () => {
      state = initialAsyncState();
      notify(state);
    },
    execute: async (asyncFn: () => Promise<T>) => {
      state = loadingAsyncState(state.data);
      notify(state);

      try {
        const result = await asyncFn();
        state = successAsyncState(result);
        notify(state);
        return result;
      } catch (error) {
        state = errorAsyncState(error as E, state.data);
        notify(state);
        throw error;
      }
    },
  };
}

export function createPagination(defaultPageSize = 10) {
  let state: PaginationState = { ...initialPaginationState(), pageSize: defaultPageSize };
  const { notify, subscribe } = createNotifier<PaginationState>();

  return {
    getState: () => state,
    subscribe,
    setPage: (pageIndex: number) => {
      state = { ...state, pageIndex: Math.max(1, pageIndex) };
      notify(state);
    },
    setPageSize: (pageSize: number) => {
      state = { ...state, pageSize, pageIndex: 1 };
      notify(state);
    },
    setTotal: (totalCount: number) => {
      const totalPages = Math.ceil(totalCount / state.pageSize);
      state = {
        ...state,
        totalCount,
        totalPages,
        hasMore: state.pageIndex < totalPages,
      };
      notify(state);
    },
    reset: () => {
      state = { ...initialPaginationState(), pageSize: defaultPageSize };
      notify(state);
    },
    getParams: () => ({
      pageIndex: state.pageIndex,
      pageSize: state.pageSize,
      skip: (state.pageIndex - 1) * state.pageSize,
      take: state.pageSize,
    }),
  };
}

export function createCounter(initialValue = 0) {
  let value = initialValue;
  const { notify, subscribe } = createNotifier<number>();

  return {
    get: () => value,
    subscribe,
    increment: (delta = 1) => {
      value += delta;
      notify(value);
    },
    decrement: (delta = 1) => {
      value -= delta;
      notify(value);
    },
    set: (nextValue: number) => {
      value = nextValue;
      notify(value);
    },
    reset: () => {
      value = initialValue;
      notify(value);
    },
  };
}

export function createToggle(initialValue = false) {
  let value = initialValue;
  const { notify, subscribe } = createNotifier<boolean>();

  return {
    get: () => value,
    subscribe,
    toggle: () => {
      value = !value;
      notify(value);
    },
    set: (nextValue: boolean) => {
      value = nextValue;
      notify(value);
    },
    setTrue: () => {
      value = true;
      notify(value);
    },
    setFalse: () => {
      value = false;
      notify(value);
    },
  };
}

export function createLocalStorage<T>(key: string, defaultValue: T) {
  const memory = createMemoryStorageAdapter();
  const { notify, subscribe } = createNotifier<T>();

  const storage =
    typeof window !== 'undefined' && window.localStorage
      ? {
          getItem: (k: string) => window.localStorage.getItem(k),
          setItem: (k: string, v: string) => window.localStorage.setItem(k, v),
          removeItem: (k: string) => window.localStorage.removeItem(k),
        }
      : memory;

  return {
    get: (): T => {
      try {
        const stored = storage.getItem(key);
        if (stored === null) return defaultValue;
        return JSON.parse(stored) as T;
      } catch {
        return defaultValue;
      }
    },
    subscribe,
    set: (value: T) => {
      try {
        storage.setItem(key, JSON.stringify(value));
        notify(value);
      } catch {
        notify(defaultValue);
      }
    },
    remove: () => {
      try {
        storage.removeItem(key);
      } finally {
        notify(defaultValue);
      }
    },
  };
}
