/**
 * @tnzi/core/components/table
 *
 * Data table component interfaces.
 */

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

/**
 * Data table Props
 */
export interface IDataTableProps<T = unknown> {
  /** Table data */
  data: T[];
  /** Column definitions */
  columns: ITableColumn<T>[];
  /** Row key field */
  rowKey?: string | ((row: T) => string);
  /** Whether loading */
  loading?: boolean;
  /** Whether selectable rows */
  selectable?: boolean;
  /** Selection type */
  selectionType?: 'checkbox' | 'radio';
  /** Selected row keys */
  selectedKeys?: string[];
  /** Pagination config, false means no pagination */
  pagination?: IPaginationConfig | false;
  /** Whether sortable */
  sortable?: boolean;
  /** Current sort field */
  sortBy?: string;
  /** Sort order */
  sortOrder?: 'asc' | 'desc';
  /** Whether show border */
  bordered?: boolean;
  /** Whether show striped */
  striped?: boolean;
  /** Empty data text */
  emptyText?: string;
  /** Table size */
  size?: 'small' | 'medium' | 'large';
  /** Action column config */
  actions?: {
    /** Action button list */
    buttons?: Array<{
      key: string;
      label: string;
      icon?: string;
      type?: 'primary' | 'default' | 'danger' | 'warning';
      disabled?: (row: T) => boolean;
      visible?: (row: T) => boolean;
    }>;
    /** Custom action column render */
    render?: (row: T, index: number) => unknown;
  };
  /** Export config */
  exportable?: {
    /** Export filename */
    filename?: string;
    /** Export format */
    format?: 'xlsx' | 'csv' | 'json';
    /** Whether include header */
    includeHeader?: boolean;
  };
  /** Print config */
  printable?: {
    /** Print title */
    title?: string;
    /** Whether include header */
    includeHeader?: boolean;
  };
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
  /** Experimental features that are not guaranteed to be implemented in all platforms */
  experimental?: {
    virtualScroll?: boolean;
    rowHeight?: number;
    maxHeight?: number | string;
    expandable?: boolean;
    expandedRowRender?: (row: T, index: number) => unknown;
    treeData?: {
      childrenField?: string;
      defaultExpandAllRows?: boolean;
      expandable?: (row: T) => boolean;
    };
    columnDraggable?: boolean;
    rowDraggable?: boolean;
  };
}

/**
 * Data table Emits
 */
export interface IDataTableEmits<T = unknown> {
  /** Selection change */
  'update:selectedKeys': [keys: string[]];
  /** Sort change */
  sort: [field: string, order: 'asc' | 'desc'];
  /** Page change */
  pageChange: [pageIndex: number, pageSize: number];
  /** Row click */
  rowClick: [row: T, index: number];
  /** Row double click */
  rowDblClick: [row: T, index: number];
  /** Action button click */
  action: [actionKey: string, row: T, index: number];
  /** Export */
  export: [format: 'xlsx' | 'csv' | 'json'];
  /** Print */
  print: [];
}
