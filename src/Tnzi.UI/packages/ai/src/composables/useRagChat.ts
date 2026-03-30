/**
 * useRagChat — RAG mode chat with knowledge base selection and citations
 */

import { ref, computed, readonly, type Ref, type DeepReadonly } from 'vue';

export interface RagCitation {
  title: string;
  url: string;
  snippet?: string;
  score?: number;
}

export interface UseRagChatReturn {
  selectedBaseIds: DeepReadonly<Ref<readonly string[]>>;
  citations: DeepReadonly<Ref<readonly RagCitation[]>>;
  isRagEnabled: DeepReadonly<Ref<boolean>>;
  toggleBase: (baseId: string) => void;
  clearBases: () => void;
  setCitations: (citations: RagCitation[]) => void;
  clearCitations: () => void;
}

export function useRagChat(): UseRagChatReturn {
  const selectedBaseIds = ref<string[]>([]);
  const citations = ref<readonly RagCitation[]>([]);
  const isRagEnabled = computed(() => selectedBaseIds.value.length > 0);

  function toggleBase(baseId: string): void {
    const idx = selectedBaseIds.value.indexOf(baseId);
    if (idx >= 0) {
      selectedBaseIds.value = [
        ...selectedBaseIds.value.slice(0, idx),
        ...selectedBaseIds.value.slice(idx + 1),
      ];
    } else {
      selectedBaseIds.value = [...selectedBaseIds.value, baseId];
    }
  }

  function clearBases(): void {
    selectedBaseIds.value = [];
  }

  function setCitations(newCitations: RagCitation[]): void {
    citations.value = [...newCitations];
  }

  function clearCitations(): void {
    citations.value = [];
  }

  return {
    selectedBaseIds: readonly(selectedBaseIds) as DeepReadonly<Ref<readonly string[]>>,
    citations: readonly(citations) as DeepReadonly<Ref<readonly RagCitation[]>>,
    isRagEnabled: isRagEnabled as unknown as DeepReadonly<Ref<boolean>>,
    toggleBase,
    clearBases,
    setCitations,
    clearCitations,
  };
}
