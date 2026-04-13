/**
 * useLocalSearch -- Reusable local text search composable
 *
 * Filters an array of items by matching a query string against
 * specified string fields. Handles undefined/null field values safely.
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue';

export interface UseLocalSearchReturn<T> {
  query: Ref<string>;
  filtered: ComputedRef<T[]>;
}

export function useLocalSearch<T>(
  items: () => T[],
  fields: (keyof T)[],
): UseLocalSearchReturn<T> {
  const query = ref('');
  const filtered = computed(() => {
    const q = query.value.toLowerCase();
    if (!q) return items();
    return items().filter((item) =>
      fields.some((field) => {
        const val = item[field];
        return typeof val === 'string' && val.toLowerCase().includes(q);
      }),
    );
  });
  return { query, filtered };
}
