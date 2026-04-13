<script setup lang="ts">
/**
 * MessageResponse — Streaming response wrapper
 *
 * Thin wrapper around StreamMarkdown that provides the standard
 * assistant response rendering with streaming cursor support.
 */

import StreamMarkdown from '../streaming/StreamMarkdown.vue';

defineProps<{
  content: string;
  streaming?: boolean;
}>();
</script>

<template>
  <StreamMarkdown
    :content="content"
    :streaming="streaming ?? false"
  />
</template>
