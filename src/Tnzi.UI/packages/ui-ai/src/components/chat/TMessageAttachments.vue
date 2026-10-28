<script setup lang="ts">
/**
 * TMessageAttachments - Attachment display
 *
 * Renders image thumbnails and file cards for message attachments: ones still in memory
 * (just sent, carrying their data) and stored ones (an `attachmentId`, read through the
 * render context's resolver).
 */

import type { MessageAttachment } from '../../headless/useChat';
import TAttachmentImage from './TAttachmentImage.vue';
import TFileCard from './TFileCard.vue';

defineProps<{
  attachments: MessageAttachment[];
}>();

function isImage(attachment: MessageAttachment): boolean {
  if (attachment.type === 'image') return true;
  if (attachment.mediaType?.startsWith('image/')) return true;
  return false;
}

/** An image at hand: its data, or a URL the browser can load. Null for a stored one. */
function getImageSrc(attachment: MessageAttachment): string | null {
  if (attachment.base64Data && attachment.mediaType) {
    return `data:${attachment.mediaType};base64,${attachment.base64Data}`;
  }
  return attachment.url ?? null;
}

</script>

<template>
  <div class="flex flex-wrap gap-2">
    <template v-for="(attachment, index) in attachments" :key="attachment.attachmentId ?? index">
      <TAttachmentImage
        v-if="isImage(attachment)"
        :attachment-id="attachment.attachmentId"
        :src="getImageSrc(attachment)"
        :alt="attachment.fileName ?? 'Image'"
      />
      <TFileCard
        v-else
        :file="{
          name: attachment.fileName,
          mediaType: attachment.mediaType,
          size: attachment.size,
          attachmentId: attachment.attachmentId,
        }"
      />
    </template>
  </div>
</template>
