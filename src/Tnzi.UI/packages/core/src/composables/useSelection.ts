/**
 * @tnzi/core/composables/useSelection
 *
 * Composable factory for SelectionController.
 *
 * @example
 * ```ts
 * const selection = useSelection<string>({ mode: 'multiple' });
 * selection.toggle('item-1');
 * // selection.selectedKeys, selection.isAllSelected, etc. are reactive
 * ```
 */

import { SelectionController } from '../headless/selection';
import type { SelectionMode, SelectionOptions } from '../headless/selection';

/**
 * Create a reactive selection controller.
 *
 * Supports single and multiple selection modes with O(1) isSelected lookup.
 * All properties are reactive and can be used directly in Vue templates.
 */
export function useSelection<TKey = string>(
  options: SelectionOptions<TKey> = {},
): SelectionController<TKey> {
  return new SelectionController<TKey>(options);
}

export type { SelectionMode, SelectionOptions };
