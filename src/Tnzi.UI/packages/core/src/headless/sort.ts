/**
 * @tnzi/core/headless/sort
 *
 * 排序控制器 - 响应式无头逻辑。
 */

import { reactive } from 'vue';

// ============================================
// Types
// ============================================

import type { SortDirection } from '../types/pagination';

export type { SortDirection };

export interface SortField {
  field: string;
  direction: SortDirection;
}

export interface SortOptions {
  /** 初始排序字段 */
  defaultField?: string;
  /** 初始排序方向 */
  defaultDirection?: SortDirection;
  /** 是否支持多字段排序 */
  multiple?: boolean;
}

// ============================================
// Sort Utility Functions (from headless-data)
// ============================================

export type HeadlessSortDirection = 'asc' | 'desc';

export interface SortState {
  sortBy?: string;
  sortDirection?: HeadlessSortDirection;
}

export function toggleSort(
  current: SortState,
  nextSortBy: string,
  initialDirection: HeadlessSortDirection = 'asc'
): Required<SortState> {
  if (current.sortBy === nextSortBy) {
    return {
      sortBy: nextSortBy,
      sortDirection: current.sortDirection === 'asc' ? 'desc' : 'asc',
    };
  }
  return {
    sortBy: nextSortBy,
    sortDirection: initialDirection,
  };
}

// ============================================
// SortController
// ============================================

/**
 * 响应式排序控制器。
 *
 * ```ts
 * const sort = new SortController({ defaultField: 'name', defaultDirection: 'asc' });
 * sort.toggle('name');       // name desc
 * sort.toggle('name');       // name asc
 * sort.toggle('createdAt');  // createdAt asc (切换到新字段)
 * ```
 */
export class SortController {
  readonly multiple: boolean;
  /** 排序字段列表 - 唯一状态源 */
  sortFields: SortField[];

  constructor(options: SortOptions = {}) {
    this.multiple = options.multiple ?? false;
    this.sortFields = options.defaultField
      ? [{ field: options.defaultField, direction: options.defaultDirection ?? 'asc' }]
      : [];
    return reactive(this) as this;
  }

  // Derived getters

  get sortBy(): string | null {
    return this.sortFields.length > 0 ? this.sortFields[0]!.field : null;
  }

  get sortDirection(): SortDirection {
    return this.sortFields.length > 0 ? this.sortFields[0]!.direction : 'asc';
  }

  get hasSorting(): boolean {
    return this.sortFields.length > 0;
  }

  get isAscending(): boolean {
    return this.sortDirection === 'asc';
  }

  get isDescending(): boolean {
    return this.sortDirection === 'desc';
  }

  // Actions

  /**
   * 切换排序：
   * - 点击当前排序字段 → 切换方向
   * - 点击新字段 → 按该字段升序
   */
  toggle(field: string): void {
    const existingIndex = this.sortFields.findIndex(f => f.field === field);

    if (!this.multiple) {
      if (existingIndex >= 0) {
        const current = this.sortFields[existingIndex]!;
        this.sortFields = [{ field, direction: current.direction === 'asc' ? 'desc' : 'asc' }];
      } else {
        this.sortFields = [{ field, direction: 'asc' }];
      }
    } else {
      if (existingIndex >= 0) {
        const current = this.sortFields[existingIndex]!;
        this.sortFields = this.sortFields.map((f, i) =>
          i === existingIndex ? { field, direction: current.direction === 'asc' ? 'desc' : 'asc' } : f
        );
      } else {
        this.sortFields = [...this.sortFields, { field, direction: 'asc' }];
      }
    }
  }

  /** 设置排序 */
  setSort(field: string, direction: SortDirection): void {
    if (!this.multiple) {
      this.sortFields = [{ field, direction }];
    } else {
      const existingIndex = this.sortFields.findIndex(f => f.field === field);
      if (existingIndex >= 0) {
        this.sortFields = this.sortFields.map((f, i) =>
          i === existingIndex ? { field, direction } : f
        );
      } else {
        this.sortFields = [...this.sortFields, { field, direction }];
      }
    }
  }

  /** 清除排序 */
  clear(): void {
    this.sortFields = [];
  }

  /** 获取指定字段的排序方向（用于 UI 图标显示） */
  getFieldDirection(field: string): SortDirection | null {
    const entry = this.sortFields.find(f => f.field === field);
    return entry?.direction ?? null;
  }

  /** 生成排序查询参数 */
  toQuery(): { sortBy?: string; sortDescending?: boolean } {
    if (this.sortFields.length === 0) return {};
    return {
      sortBy: this.sortFields[0]!.field,
      sortDescending: this.sortFields[0]!.direction === 'desc',
    };
  }
}
