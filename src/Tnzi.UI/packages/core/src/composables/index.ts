/**
 * @tnzi/core/composables
 *
 * Composable factory functions for headless controllers.
 * Each composable returns a fully reactive instance built on @vue/reactivity.
 *
 * These are thin wrappers over the headless controllers that follow Vue's
 * `useXxx` naming convention for better discoverability and ergonomics.
 */

export { useDataQuery } from './useDataQuery';
export type { DataQueryOptions } from './useDataQuery';

export { useForm } from './useForm';
export type { FormOptions, FormFieldError } from './useForm';

export { usePagination } from './usePagination';
export type { PaginationOptions } from './usePagination';

export { useSelection } from './useSelection';
export type { SelectionMode, SelectionOptions } from './useSelection';

export { useSort } from './useSort';
export type { SortDirection, SortField, SortOptions } from './useSort';
