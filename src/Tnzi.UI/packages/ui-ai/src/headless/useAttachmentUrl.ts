/**
 * `useAttachmentUrl` - a displayable URL for a stored chat attachment.
 *
 * Stored attachments are read with the signed-in client (a plain `<img src>` carries no
 * Authorization header), so a renderer asks a resolver for an object URL and shows it once
 * it arrives. The resolver owns the URLs and releases them (`useChatThreads` does so when
 * the thread changes); this only tracks which one belongs to the current id.
 */
import { ref, watch, type Ref } from 'vue';

export interface UseAttachmentUrlReturn {
  /** The URL once resolved; null while loading, without a resolver, or when it cannot be read. */
  url: Ref<string | null>;
  /** A resolve is in flight. */
  loading: Ref<boolean>;
  /** The attachment could not be read (or there is no resolver). */
  failed: Ref<boolean>;
}

export function useAttachmentUrl(
  attachmentId: () => string | null | undefined,
  resolver: () => ((attachmentId: string) => Promise<string | null>) | undefined,
): UseAttachmentUrlReturn {
  const url = ref<string | null>(null);
  const loading = ref(false);
  const failed = ref(false);
  let seq = 0;

  watch(
    [attachmentId, resolver],
    async ([id, resolve]) => {
      const current = ++seq;
      url.value = null;
      failed.value = false;
      if (!id) {
        loading.value = false;
        return;
      }
      if (!resolve) {
        loading.value = false;
        failed.value = true;
        return;
      }
      loading.value = true;
      let resolved: string | null = null;
      try {
        resolved = await resolve(id);
      } catch {
        resolved = null;
      }
      // A newer id took over while this one was loading: its answer is not ours to show
      if (current !== seq) return;
      url.value = resolved;
      failed.value = resolved === null;
      loading.value = false;
    },
    { immediate: true },
  );

  return { url, loading, failed };
}
