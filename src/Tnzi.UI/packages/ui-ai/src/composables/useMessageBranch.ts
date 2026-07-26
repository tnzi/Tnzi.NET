/**
 * useMessageBranch - Branch navigation for message variants
 *
 * Manages multiple response branches (e.g., after regeneration) with
 * circular navigation. Useful for showing "1/3" style branch selectors.
 */

import { ref, readonly, computed, type Ref, type DeepReadonly, type ComputedRef } from 'vue';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseMessageBranchReturn {
  /** Current branch index (0-based). */
  currentBranch: DeepReadonly<Ref<number>>;
  /** Total number of branches. */
  totalBranches: DeepReadonly<Ref<number>>;
  /** Display text, e.g. "1/3". */
  displayText: ComputedRef<string>;
  /** Navigate to the next branch (wraps around). */
  goToNext: () => void;
  /** Navigate to the previous branch (wraps around). */
  goToPrev: () => void;
  /** Add a new branch and navigate to it. */
  addBranch: () => void;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useMessageBranch(initialCount = 1): UseMessageBranchReturn {
  const currentBranch = ref(0);
  const totalBranches = ref(Math.max(1, initialCount));

  const displayText = computed(() =>
    `${currentBranch.value + 1}/${totalBranches.value}`,
  );

  function goToNext(): void {
    if (totalBranches.value <= 1) return;
    currentBranch.value = (currentBranch.value + 1) % totalBranches.value;
  }

  function goToPrev(): void {
    if (totalBranches.value <= 1) return;
    currentBranch.value =
      (currentBranch.value - 1 + totalBranches.value) % totalBranches.value;
  }

  function addBranch(): void {
    totalBranches.value = totalBranches.value + 1;
    currentBranch.value = totalBranches.value - 1;
  }

  return {
    currentBranch: readonly(currentBranch),
    totalBranches: readonly(totalBranches),
    displayText,
    goToNext,
    goToPrev,
    addBranch,
  };
}
