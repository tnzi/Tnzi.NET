/**
 * Store Types - State management type definitions
 */

/**
 * Async state status
 */
export type AsyncStatus = 'idle' | 'loading' | 'success' | 'error';

/**
 * Generic async state
 */
export interface AsyncState<T, E = Error> {
  data: T | null;
  status: AsyncStatus;
  error: E | null;
  isLoading: boolean;
  isSuccess: boolean;
  isError: boolean;
  isIdle: boolean;
}

/**
 * Create initial async state
 */
export function initialAsyncState<T, E = Error>(): AsyncState<T, E> {
  return {
    data: null,
    status: 'idle',
    error: null,
    isLoading: false,
    isSuccess: false,
    isError: false,
    isIdle: true,
  };
}

/**
 * Create loading async state
 */
export function loadingAsyncState<T, E = Error>(previousData: T | null = null): AsyncState<T, E> {
  return {
    data: previousData,
    status: 'loading',
    error: null,
    isLoading: true,
    isSuccess: false,
    isError: false,
    isIdle: false,
  };
}

/**
 * Create success async state
 */
export function successAsyncState<T, E = Error>(data: T): AsyncState<T, E> {
  return {
    data,
    status: 'success',
    error: null,
    isLoading: false,
    isSuccess: true,
    isError: false,
    isIdle: false,
  };
}

/**
 * Create error async state
 */
export function errorAsyncState<T, E = Error>(error: E, previousData: T | null = null): AsyncState<T, E> {
  return {
    data: previousData,
    status: 'error',
    error,
    isLoading: false,
    isSuccess: false,
    isError: true,
    isIdle: false,
  };
}

/**
 * Pagination state
 */
export interface PaginationState {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasMore: boolean;
}

/**
 * Create initial pagination state
 */
export function initialPaginationState(): PaginationState {
  return {
    pageIndex: 1,
    pageSize: 10,
    totalCount: 0,
    totalPages: 0,
    hasMore: false,
  };
}

/**
 * Form field state
 */
export interface FormFieldState<T = unknown> {
  value: T;
  touched: boolean;
  dirty: boolean;
  error?: string;
}

/**
 * Form state
 */
export interface FormState<T extends Record<string, unknown>> {
  values: T;
  touched: Record<keyof T, boolean>;
  dirty: Record<keyof T, boolean>;
  errors: Partial<Record<keyof T, string>>;
  isValid: boolean;
  isSubmitting: boolean;
  isDirty: boolean;
}

/**
 * Notification/Toast state
 */
export interface NotificationState {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  title?: string;
  message: string;
  duration?: number;
  timestamp: Date;
}

/**
 * Modal state
 */
export interface ModalState<T = unknown> {
  isOpen: boolean;
  data: T | null;
}
