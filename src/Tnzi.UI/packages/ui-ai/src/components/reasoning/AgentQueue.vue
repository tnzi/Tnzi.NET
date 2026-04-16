<script setup lang="ts">
/**
 * AgentQueue — Task queue with collapsible sections
 */

import { NScrollbar } from 'naive-ui';
import { reactive } from 'vue';
import { Icon } from '@iconify/vue';

export interface QueueSection {
  label: string;
  icon?: string;
  items: QueueItem[];
  defaultOpen?: boolean;
}

export interface QueueItem {
  id: string;
  title: string;
  description?: string;
  completed?: boolean;
  images?: string[];
  files?: string[];
}

const props = defineProps<{
  sections: QueueSection[];
  maxHeight?: number;
}>();

const emit = defineEmits<{
  'item-click': [item: QueueItem];
}>();

// 每个 section 独立的 open 状态（用 label 作 key）
const openSections = reactive(new Set<string>(
  props.sections.filter((s) => s.defaultOpen !== false).map((s) => s.label),
));

function toggleSection(label: string): void {
  if (openSections.has(label)) {
    openSections.delete(label);
  } else {
    openSections.add(label);
  }
}
</script>

<template>
  <div class="t-agent-queue">
    <NScrollbar :style="{ maxHeight: `${maxHeight ?? 200}px` }">
      <div class="t-agent-queue__body">
        <div
          v-for="section in sections"
          :key="section.label"
          class="t-agent-queue__section"
        >
          <button
            type="button"
            class="t-agent-queue__section-btn"
            @click="toggleSection(section.label)"
          >
            <Icon v-if="section.icon" :icon="section.icon" class="size-3.5 shrink-0" />
            <span class="flex-1 text-left">{{ section.label }}</span>
            <span class="text-[10px] tabular-nums">{{ section.items.length }}</span>
            <Icon
              icon="lucide:chevron-down"
              class="size-3 shrink-0 t-agent-queue__chevron"
              :class="{ 't-agent-queue__chevron--collapsed': !openSections.has(section.label) }"
            />
          </button>
          <ul v-show="openSections.has(section.label)" class="t-agent-queue__items">
            <li
              v-for="item in section.items"
              :key="item.id"
              class="t-agent-queue__item"
              @click="emit('item-click', item)"
            >
              <span class="t-agent-queue__dot" :class="{ 't-agent-queue__dot--done': item.completed }">
                <Icon v-if="item.completed" icon="lucide:check" class="size-2.5" />
              </span>
              <div class="flex-1 min-w-0">
                <span
                  class="block truncate"
                  :class="{ 't-agent-queue__title--done': item.completed }"
                >{{ item.title }}</span>
                <span
                  v-if="item.description"
                  class="block text-xs t-agent-queue__muted mt-0.5 truncate"
                  :class="{ 't-agent-queue__title--done': item.completed }"
                >{{ item.description }}</span>
                <div v-if="(item.images?.length ?? 0) > 0 || (item.files?.length ?? 0) > 0" class="flex flex-wrap gap-1 mt-1.5">
                  <img v-for="(img, i) in item.images" :key="`img-${i}`" :src="img" class="size-8 rounded object-cover" />
                  <span v-for="(file, i) in item.files" :key="`file-${i}`" class="t-agent-queue__file-badge">
                    <Icon icon="lucide:paperclip" class="size-3" />
                    <span class="max-w-[80px] truncate">{{ file }}</span>
                  </span>
                </div>
              </div>
            </li>
          </ul>
        </div>
      </div>
    </NScrollbar>
  </div>
</template>

<style scoped>
.t-agent-queue {
  border-radius: 12px;
  border: 1px solid var(--tnzi-border);
  overflow: hidden;
}
.t-agent-queue__body { /* divide-y */ }
.t-agent-queue__section { border-top: 1px solid var(--tnzi-border); }
.t-agent-queue__section:first-child { border-top: none; }
.t-agent-queue__section-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-base-text-muted);
  background: none;
  border: none;
  cursor: pointer;
  transition: background-color 0.15s;
}
.t-agent-queue__section-btn:hover { background-color: var(--tnzi-hover-bg, rgba(0,0,0,0.04)); }
.t-agent-queue__chevron { transition: transform 0.2s; }
.t-agent-queue__chevron--collapsed { transform: rotate(-90deg); }
.t-agent-queue__items { border-top: 1px solid var(--tnzi-border); }
.t-agent-queue__item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 12px;
  font-size: 14px;
  cursor: pointer;
  transition: background-color 0.15s;
  border-top: 1px solid color-mix(in srgb, var(--tnzi-border) 50%, transparent);
}
.t-agent-queue__item:first-child { border-top: none; }
.t-agent-queue__item:hover { background-color: var(--tnzi-hover-bg, rgba(0,0,0,0.03)); }
.t-agent-queue__dot {
  margin-top: 4px;
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  border-radius: 50%;
  border: 2px solid var(--tnzi-border);
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
}
.t-agent-queue__dot--done {
  border-color: var(--tnzi-primary);
  background-color: var(--tnzi-primary);
}
.t-agent-queue__muted { color: var(--tnzi-base-text-muted); }
.t-agent-queue__title--done {
  text-decoration: line-through;
  color: var(--tnzi-base-text-muted);
}
.t-agent-queue__file-badge {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  border-radius: 4px;
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  padding: 1px 6px;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
</style>
