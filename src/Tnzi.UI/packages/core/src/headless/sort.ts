/**
 * @tnzi/core/headless/sort
 *
 * 排序控制器 — 响应式无头逻辑。
 */

import { reactive } from '@vue/reactivity';

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
  sortBy: string | null;
  sortDirection: SortDirection;
  readonly multiple: boolean;
  /** 多字段排序列表（multiple=true 时使用） */
  sortFields: SortField[];

  constructor(options: SortOptions = {}) {
    this.sortBy = options.defaultField ?? null;
    this.sortDirection = options.defaultDirection ?? 'asc';
    this.multiple = options.multiple ?? false;
    this.sortFields = this.sortBy
      ? [{ field: this.sortBy, direction: this.sortDirection }]
      : [];
    return reactive(this) as this;
  }

  // Getters
  get hasSorting(): boolean {
    return this.sortBy !== null;
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
    if (this.sortBy === field) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortBy = field;
      this.sortDirection = 'asc';
    }

    // 更新 sortFields
    if (!this.multiple) {
      this.sortFields = this.sortBy
        ? [{ field: this.sortBy, direction: this.sortDirection }]
        : [];
    }
  }

  /** 设置排序 */
  setSort(field: string, direction: SortDirection): void {
    this.sortBy = field;
    this.sortDirection = direction;
    if (!this.multiple) {
      this.sortFields = [{ field, direction }];
    }
  }

  /** 清除排序 */
  clear(): void {
    this.sortBy = null;
    this.sortDirection = 'asc';
    this.sortFields = [];
  }

  /** 获取指定字段的排序方向（用于 UI 图标显示） */
  getFieldDirection(field: string): SortDirection | null {
    if (this.sortBy === field) return this.sortDirection;
    const entry = this.sortFields.find(f => f.field === field);
    return entry?.direction ?? null;
  }

  /** 生成排序查询参数 */
  toQuery(): { sortBy?: string; sortDescending?: boolean } {
    if (!this.sortBy) return {};
    return {
      sortBy: this.sortBy,
      sortDescending: this.sortDirection === 'desc',
    };
  }
}
