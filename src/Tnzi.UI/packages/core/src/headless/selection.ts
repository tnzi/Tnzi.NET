/**
 * @tnzi/core/headless/selection
 *
 * Selection state controller - reactive headless logic.
 */

import { reactive } from 'vue';

// ============================================
// Types
// ============================================

export type SelectionMode = 'single' | 'multiple';

export interface SelectionOptions<TKey = string> {
  /** Selection mode */
  mode?: SelectionMode;
  /** Initial selected keys */
  initialKeys?: TKey[];
}

// ============================================
// SelectionController
// ============================================

/**
 * Reactive selection state controller.
 *
 * ```ts
 * const selection = new SelectionController<string>();
 * selection.toggle('item-1');
 * selection.selectedKeys  // ['item-1']
 * selection.isAllSelected // reactive
 * ```
 */
export class SelectionController<TKey = string> {
  /** Selected keys as array (for Vue reactivity) */
  selectedKeys: TKey[];
  private _allKeys: TKey[] = [];
  readonly mode: SelectionMode;

  constructor(options: SelectionOptions<TKey> = {}) {
    this.mode = options.mode ?? 'multiple';
    this.selectedKeys = options.initialKeys ? [...options.initialKeys] : [];
    return reactive(this) as this;
  }

  // Getters
  get selectedCount(): number {
    return this.selectedKeys.length;
  }

  get hasSelection(): boolean {
    return this.selectedKeys.length > 0;
  }

  /**
   * Whether every key of the current set (see {@link setAllKeys}) is selected.
   *
   * Membership, not a count: with cross-page selection the selected keys also
   * hold rows from other pages, and "20 selected, 20 on this page" said
   * nothing about whether the 20 on this page were the selected ones.
   */
  get isAllSelected(): boolean {
    return this._allKeys.length > 0 && this._allKeys.every((key) => this.selectedKeys.includes(key));
  }

  /**
   * Some but not all keys of the current set are selected. Before any key set
   * is known, any selection counts as partial.
   */
  get isIndeterminate(): boolean {
    if (this._allKeys.length === 0) return this.selectedKeys.length > 0;
    return !this.isAllSelected && this._allKeys.some((key) => this.selectedKeys.includes(key));
  }

  // Actions

  /** Set all available keys (used for "select all" feature) */
  setAllKeys(keys: TKey[]): void {
    // Deduplicate
    this._allKeys = [...new Set(keys)];
  }

  /** Select a key */
  select(key: TKey): void {
    if (this.mode === 'single') {
      this.selectedKeys = [key];
      return;
    }
    if (!this.selectedKeys.includes(key)) {
      this.selectedKeys = [...this.selectedKeys, key];
    }
  }

  /** Deselect a key */
  deselect(key: TKey): void {
    this.selectedKeys = this.selectedKeys.filter(k => k !== key);
  }

  /** Toggle selection state of a key */
  toggle(key: TKey): void {
    if (this.isSelected(key)) {
      this.deselect(key);
    } else {
      this.select(key);
    }
  }

  /**
   * Select every key of the current set, or deselect them when all already
   * are. Keys outside the current set (rows ticked on another page) are kept
   * either way.
   */
  toggleAll(): void {
    if (this.isAllSelected) {
      const current = new Set(this._allKeys);
      this.selectedKeys = this.selectedKeys.filter((key) => !current.has(key));
    } else {
      const missing = this._allKeys.filter((key) => !this.selectedKeys.includes(key));
      this.selectedKeys = [...this.selectedKeys, ...missing];
    }
  }

  /** Check if a key is selected */
  isSelected(key: TKey): boolean {
    return this.selectedKeys.includes(key);
  }

  /** Clear all selections */
  clear(): void {
    this.selectedKeys = [];
  }

  /** Replace selected keys */
  setSelectedKeys(keys: TKey[]): void {
    this.selectedKeys = [...keys];
  }
}
