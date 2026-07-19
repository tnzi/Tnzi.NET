<script setup lang="ts">
/**
 * TMessageResponse - Streaming response wrapper
 *
 * Thin wrapper around TStreamMarkdown that provides the standard
 * assistant response rendering with streaming cursor support.
 */

import TStreamMarkdown from '../streaming/TStreamMarkdown.vue';

defineProps<{
  content: string;
  streaming?: boolean;
}>();
</script>

<template>
  <TStreamMarkdown
    :content="content"
    :streaming="streaming ?? false"
  />
</template>
