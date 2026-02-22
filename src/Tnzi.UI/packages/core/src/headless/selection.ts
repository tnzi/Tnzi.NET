/**
 * @tnzi/core/headless/selection
 *
 * Selection state controller - reactive headless logic.
 */

import { reactive } from '@vue/reactivity';

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
  private _selectedSet: Set<TKey>;
  readonly mode: SelectionMode;

  constructor(options: SelectionOptions<TKey> = {}) {
    this.mode = options.mode ?? 'multiple';
    const initial = options.initialKeys ? [...options.initialKeys] : [];
    this.selectedKeys = initial;
    this._selectedSet = new Set(initial);
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
      this._selectedSet = new Set([key]);
      return;
    }
    if (!this._selectedSet.has(key)) {
      this.selectedKeys.push(key);
      this._selectedSet.add(key);
    }
  }

  /** Deselect a key */
  deselect(key: TKey): void {
    this.selectedKeys = this.selectedKeys.filter(k => k !== key);
    this._selectedSet.delete(key);
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
      this._selectedSet = new Set(this._allKeys);
    }
  }

  /** Check if a key is selected (O(1) lookup) */
  isSelected(key: TKey): boolean {
    return this._selectedSet.has(key);
  }

  /** Clear all selections */
  clear(): void {
    this.selectedKeys = [];
    this._selectedSet = new Set();
  }

  /** Replace selected keys */
  setSelectedKeys(keys: TKey[]): void {
    this.selectedKeys = [...keys];
    this._selectedSet = new Set(keys);
  }
}
