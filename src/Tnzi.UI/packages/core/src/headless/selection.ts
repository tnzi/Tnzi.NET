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

  get isAllSelected(): boolean {
    return this._allKeys.length > 0 && this.selectedKeys.length === this._allKeys.length;
  }

  get isIndeterminate(): boolean {
    return this.selectedKeys.length > 0 && !this.isAllSelected;
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

  /** Toggle select all / deselect all */
  toggleAll(): void {
    if (this.isAllSelected) {
      this.clear();
    } else {
      this.selectedKeys = [...this._allKeys];
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
