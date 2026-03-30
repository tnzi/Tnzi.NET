<script setup lang="ts">
/**
 * TOpenIn — Open in external platform dropdown
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator } from '@/primitives/ui/dropdown-menu';
import { Button } from '@/primitives/ui/button';

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

function openPlatform(platform: ExternalPlatform): void {
  window.open(platform.createUrl(props.query), '_blank', 'noopener,noreferrer');
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <slot name="trigger">
        <Button variant="outline" size="sm" class="gap-1.5">
          <Icon icon="lucide:message-circle" class="size-4" />
          {{ t.openIn.label }}
          <Icon icon="lucide:chevron-down" class="size-3" />
        </Button>
      </slot>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="start" class="w-[240px]">
      <DropdownMenuLabel>{{ t.openIn.label }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem v-for="platform in platforms" :key="platform.id" class="gap-2" @click="openPlatform(platform)">
        <Icon :icon="platform.icon" class="size-4" />
        <span class="flex-1">{{ platform.name }}</span>
        <Icon icon="lucide:external-link" class="size-3 text-muted-foreground" />
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
