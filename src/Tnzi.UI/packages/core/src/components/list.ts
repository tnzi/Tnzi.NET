/**
 * @tnzi/core/components/list
 *
 * List component interfaces.
 */

/**
 * Infinite list Props
 */
export interface IInfiniteListProps<T = unknown> {
  /** List data */
  data: T[];
  /** Whether loading */
  loading?: boolean;
  /** Whether no more data */
  noMore?: boolean;
  /** Whether initial loading */
  initialLoading?: boolean;
  /** Load more trigger type */
  triggerType?: 'scroll' | 'click' | 'both';
  /** Loading text */
  loadingText?: string;
  /** No more text */
  noMoreText?: string;
  /** Empty text */
  emptyText?: string;
  /** Item key field */
  itemKey?: string | ((item: T, index: number) => string);
  /** Item render function */
  render?: (item: T, index: number) => unknown;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Infinite list Emits
 */
export interface IInfiniteListEmits<T = unknown> {
  /** Load more */
  loadMore: [];
  /** Item click */
  itemClick: [item: T, index: number];
}

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

/**
 * Swipe list Props
 */
export interface ISwipeListProps<T = unknown> {
  /** List data */
  data: T[];
  /** Left swipe actions */
  leftActions?: ISwipeAction[];
  /** Right swipe actions */
  rightActions?: ISwipeAction[];
  /** Action width (px) */
  actionWidth?: number;
  /** Whether disabled swipe */
  disabled?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Swipe list Emits
 */
export interface ISwipeListEmits<T = unknown> {
  /** Item click */
  itemClick: [item: T, index: number];
  /** Action click */
  actionClick: [actionKey: string, item: T, index: number];
}
