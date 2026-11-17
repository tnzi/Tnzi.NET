<script setup lang="ts">
/**
 * TOpenIn - Open in external platform dropdown
 *
 * A `TDropdownMenu` of chat products; picking one opens it in a new tab with
 * `query` filled in (`noopener,noreferrer`) and emits `select` with the
 * platform's id and the URL it opened.
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TButton from '../controls/TButton.vue';
import TDropdownMenu from '../overlay/TDropdownMenu.vue';
import TMenuItem from '../overlay/TMenuItem.vue';
const t = useAiI18n();

const props = defineProps<{
  query: string;
}>();

const emit = defineEmits<{
  select: [platformId: string, url: string];
}>();

interface ExternalPlatform {
  id: string;
  name: string;
  icon: string;
  createUrl: (query: string) => string;
}

const platforms: ExternalPlatform[] = [
  { id: 'chatgpt', name: 'ChatGPT', icon: 'simple-icons:openai', createUrl: (q) => `https://chatgpt.com/?q=${encodeURIComponent(q)}` },
  { id: 'claude', name: 'Claude', icon: 'simple-icons:anthropic', createUrl: (q) => `https://claude.ai/new?q=${encodeURIComponent(q)}` },
  { id: 'cursor', name: 'Cursor', icon: 'simple-icons:cursor', createUrl: (q) => `https://cursor.com/chat?q=${encodeURIComponent(q)}` },
  { id: 'github', name: 'GitHub Copilot', icon: 'simple-icons:github', createUrl: (q) => `https://github.com/copilot?q=${encodeURIComponent(q)}` },
  { id: 'scira', name: 'Scira', icon: 'lucide:search', createUrl: (q) => `https://scira.ai/search?q=${encodeURIComponent(q)}` },
  { id: 'v0', name: 'v0', icon: 'simple-icons:vercel', createUrl: (q) => `https://v0.dev/chat?q=${encodeURIComponent(q)}` },
];

function openPlatform(platform: ExternalPlatform): void {
  const url = platform.createUrl(props.query);
  window.open(url, '_blank', 'noopener,noreferrer');
  emit('select', platform.id, url);
}
</script>

<template>
  <TDropdownMenu align="left" :min-width="200">
    <template #trigger="{ toggle, attrs }">
      <TButton icon="lucide:message-circle" v-bind="attrs" @click="toggle">
        <span class="t-open-in__label">
          {{ t.openIn.label }}
          <Icon icon="lucide:chevron-down" class="t-open-in__chevron" aria-hidden="true" />
        </span>
      </TButton>
    </template>
    <template #default="{ close }">
      <TMenuItem
        v-for="platform in platforms"
        :key="platform.id"
        :icon="platform.icon"
        :data-platform="platform.id"
        @click="openPlatform(platform); close()"
      >
        {{ platform.name }}
      </TMenuItem>
    </template>
  </TDropdownMenu>
</template>

<style scoped>
.t-open-in__label {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.t-open-in__chevron {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
</style>
