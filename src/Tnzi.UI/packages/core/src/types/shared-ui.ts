/**
 * @tnzi/core/types/shared-ui
 *
 * Shared UI data structures used across all Tnzi UI packages.
 * These are pure DTOs — no Props/Emits contracts.
 */

// ============================================
// Form
// ============================================

/**
 * Form rule
 */
export interface IFormRule {
  /** Whether required */
  required?: boolean;
  /** Error message */
  message?: string;
  /** Trigger timing */
  trigger?: 'blur' | 'change' | 'input';
  /** Minimum length */
  min?: number;
  /** Maximum length */
  max?: number;
  /** Regular expression */
  pattern?: RegExp;
  /** Custom validator */
  validator?: (value: unknown) => boolean | string | Promise<boolean | string>;
}

/**
 * Dynamic form field definition
 */
export interface IDynamicFormField {
  /** Field key */
  key: string;
  /** Field label */
  label?: string;
  /** Field type */
  type:
    | 'text'
    | 'password'
    | 'email'
    | 'number'
    | 'textarea'
    | 'select'
    | 'checkbox'
    | 'radio'
    | 'date'
    | 'datetime'
    | 'file'
    | 'switch';
  /** Field placeholder */
  placeholder?: string;
  /** Whether required */
  required?: boolean;
  /** Disabled */
  disabled?: boolean;
  /** Options for select/radio/checkbox */
  options?: Array<{ label: string; value: string | number }>;
  /** Minimum value */
  min?: number;
  /** Maximum value */
  max?: number;
  /** Validation rules */
  rules?: IFormRule[];
  /** Additional props */
  props?: Record<string, unknown>;
}

// ============================================
// Table
// ============================================

/**
 * Pagination configuration
 */
export interface IPaginationConfig {
  /** Current page number (starts from 1) */
  pageIndex: number;
  /** Page size */
  pageSize: number;
  /** Total records */
  total: number;
  /** Page size options */
  pageSizes?: number[];
  /** Whether to show total */
  showTotal?: boolean;
}

/**
 * Table column definition
 */
export interface ITableColumn<T = unknown> {
  /** Column key (corresponds to data field) */
  key: string;
  /** Column title */
  title: string;
  /** Column width */
  width?: number | string;
  /** Minimum column width */
  minWidth?: number;
  /** Whether sortable */
  sortable?: boolean;
  /** Alignment */
  align?: 'left' | 'center' | 'right';
  /** Whether fixed column */
  fixed?: 'left' | 'right';
  /** Custom render function */
  render?: (row: T, index: number) => unknown;
  /** Whether hidden by default */
  hidden?: boolean;
  /** Column drag sort order */
  order?: number;
}

// ============================================
// Layout
// ============================================

/**
 * Menu item
 */
export interface IMenuItem {
  /** Item key */
  key: string;
  /** Item label */
  label: string;
  /** Icon name */
  icon?: string;
  /** Parent key */
  parentKey?: string;
  /** Route path */
  path?: string;
  /** Children items */
  children?: IMenuItem[];
  /** Badge count */
  badge?: number;
  /** Whether disabled */
  disabled?: boolean;
  /** Whether hidden */
  hidden?: boolean;
}

// ============================================
// List
// ============================================

/**
 * Swipe list item actions
 */
export interface ISwipeAction {
  /** Action key */
  key: string;
  /** Action label */
  label: string;
  /** Action type */
  type?: 'default' | 'primary' | 'danger' | 'warning';
}

// ============================================
// Navigation
// ============================================

/**
 * Tab item
 */
export interface ITabItem {
  /** Tab key */
  key: string;
  /** Tab label */
  label: string;
  /** Icon name */
  icon?: string;
  /** Badge count */
  badge?: number;
  /** Whether disabled */
  disabled?: boolean;
}

/**
 * Breadcrumb item
 */
export interface IBreadcrumbItem {
  /** Item key */
  key: string;
  /** Item label */
  label: string;
  /** Route path */
  path?: string;
  /** Whether clickable */
  clickable?: boolean;
}

// ============================================
// Data
// ============================================

/**
 * Generic list query model.
 */
export interface IDataQuery {
  /** Page index (starts from 1) */
  pageIndex?: number;
  /** Page size */
  pageSize?: number;
  /** Cursor for cursor-based pagination */
  cursor?: string;
  /** Keyword search */
  keyword?: string;
  /** Filter bag */
  filters?: Record<string, unknown>;
  /** Sort field */
  sortBy?: string;
  /** Sort direction */
  sortDirection?: 'asc' | 'desc';
}

/**
 * Generic load state model.
 */
export interface IDataLoadState {
  /** Loading list data */
  loading?: boolean;
  /** Refreshing list data */
  refreshing?: boolean;
  /** No more data */
  noMore?: boolean;
  /** Empty result */
  empty?: boolean;
  /** Optional error message */
  error?: string;
}

/**
 * Web pager configuration.
 */
export interface IWebPagerConfig {
  /** Current page (starts from 1) */
  pageIndex: number;
  /** Page size */
  pageSize: number;
  /** Total records */
  total: number;
  /** Page size options */
  pageSizes?: number[];
  /** Show quick jumper */
  showQuickJumper?: boolean;
  /** Show total */
  showTotal?: boolean;
}

/**
 * Mobile list load trigger mode.
 */
export type MobileLoadTrigger = 'scroll' | 'pull' | 'manual' | 'hybrid';

// ============================================
// Auth
// ============================================

export type OAuthSocialProvider =
  | 'Google'
  | 'Microsoft'
  | 'Facebook'
  | 'Twitter'
  | 'GitHub';

// Keep a tiny runtime marker so this subpath does not emit an empty JS chunk.
export const SHARED_UI_VERSION = '2.0.0';
