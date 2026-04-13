<script setup lang="ts">
/**
 * OpenIn — Open in external platform dropdown
 */

import { DropdownMenu, Button } from '../../primitives';
import { h } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

const props = defineProps<{
  query: string;
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

const options = [
  {
    key: 'header',
    type: 'render',
    render: () => h('div', { class: 'px-2 py-1.5 text-xs font-semibold text-muted-foreground' }, t.value.openIn.label),
  },
  { type: 'divider', key: 'd1' },
  ...platforms.map((p) => ({
    key: p.id,
    label: p.name,
    icon: () => h(Icon, { icon: p.icon, class: 'size-4' }),
  })),
];

function handleSelect(key: string): void {
  const platform = platforms.find((p) => p.id === key);
  if (platform) {
    window.open(platform.createUrl(props.query), '_blank', 'noopener,noreferrer');
  }
}
</script>

<template>
  <DropdownMenu :options="options" trigger="click" @select="handleSelect">
    <span class="inline-flex">
      <Button variant="outline" size="sm" class="gap-1.5">
        <Icon icon="lucide:message-circle" class="size-4" />
        {{ t.openIn.label }}
        <Icon icon="lucide:chevron-down" class="size-3" />
      </Button>
    </span>
  </DropdownMenu>
</template>
