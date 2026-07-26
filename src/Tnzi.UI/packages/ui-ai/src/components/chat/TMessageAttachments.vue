<script setup lang="ts">
/**
 * TMessageAttachments - Attachment display
 *
 * Renders image thumbnails and file badges for message attachments.
 */

import { Icon } from '@iconify/vue';
import { formatFileSize } from '@tnzi/core';
import type { MessageAttachment } from '@/composables/useChat';

defineProps<{
  attachments: MessageAttachment[];
}>();

function isImage(attachment: MessageAttachment): boolean {
  if (attachment.type === 'image') return true;
  if (attachment.mediaType?.startsWith('image/')) return true;
  return false;
}

function getImageSrc(attachment: MessageAttachment): string {
  if (attachment.base64Data && attachment.mediaType) {
    return `data:${attachment.mediaType};base64,${attachment.base64Data}`;
  }
  return attachment.url ?? '';
}

function getFileIcon(attachment: MessageAttachment): string {
  const name = attachment.fileName?.toLowerCase() ?? '';
  if (name.endsWith('.pdf')) return 'lucide:file-text';
  if (name.endsWith('.zip') || name.endsWith('.rar') || name.endsWith('.7z')) return 'lucide:file-archive';
  if (name.endsWith('.csv') || name.endsWith('.xlsx') || name.endsWith('.xls')) return 'lucide:file-spreadsheet';
  return 'lucide:file';
}
</script>

<template>
  <div class="flex flex-wrap gap-2">
    <template v-for="(attachment, index) in attachments" :key="index">
      <!-- Image thumbnail -->
      <div
        v-if="isImage(attachment)"
        class="relative overflow-hidden rounded-lg border border-border"
      >
        <img
          :src="getImageSrc(attachment)"
          :alt="attachment.fileName ?? 'Image'"
          class="h-20 w-20 object-cover"
          loading="lazy"
        />
      </div>

      <!-- File badge -->
      <div
        v-else
        class="flex items-center gap-2 rounded-lg border border-border bg-muted/50 px-3 py-2"
      >
        <Icon :icon="getFileIcon(attachment)" class="size-4 shrink-0 text-muted-foreground" />
        <div class="min-w-0">
          <div class="truncate text-sm font-medium">
            {{ attachment.fileName ?? 'File' }}
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
