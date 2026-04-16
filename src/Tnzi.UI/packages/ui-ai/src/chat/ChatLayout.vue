<script setup lang="ts">
/**
 * ChatLayout — Full chat layout: sidebar + main + artifact panel
 *
 * Uses flex layout with collapsible sidebar. Sidebar collapsible on mobile via NDrawer.
 */

import { NDrawer, NDrawerContent } from 'naive-ui';
import { ref } from 'vue';
import type { ChatMessage } from '@/composables/useChat';
import type { FeedbackValue } from '@/components/chat/MessageFeedback.vue';
import type { SuggestionItem } from '@/components/chat/Suggestions.vue';
import ChatSidebar, { type ThreadItem } from './ChatSidebar.vue';
import ChatMain from './ChatMain.vue';
import ChatArtifactPanel from './ChatArtifactPanel.vue';
import ChatSettings, { type ChatSettingsData } from './ChatSettings.vue';

defineProps<{
  threads: ThreadItem[];
  activeThreadId?: string;
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
  suggestions?: SuggestionItem[];
  agentName?: string;
  threadTitle?: string;
  settings?: ChatSettingsData;
  availableModels?: Array<{ id: string; name: string }>;
  /** Artifact data (show panel when set). */
  artifact?: {
    title: string;
    code?: string;
    language?: string;
    previewUrl?: string;
    previewHtml?: string;
  } | null;
}>();

const emit = defineEmits<{
  'new-chat': [];
  'select-thread': [threadId: string];
  'delete-thread': [threadId: string];
  send: [content: string, files: File[]];
  stop: [];
  copy: [messageId: string];
  regenerate: [messageId: string];
  edit: [messageId: string];
  feedback: [messageId: string, type: FeedbackValue, reason?: string];
  'update:inputText': [value: string];
  'update:settings': [settings: ChatSettingsData];
  export: [];
  'close-artifact': [];
  'download-artifact': [];
}>();

const sidebarOpen = ref(true);
const settingsOpen = ref(false);
const mobileSheetOpen = ref(false);
</script>

<template>
  <div class="t-chat-layout">
    <!-- Mobile sidebar drawer -->
    <NDrawer v-model:show="mobileSheetOpen" placement="left" :width="280">
      <NDrawerContent>
        <ChatSidebar
          :threads="threads"
          :active-thread-id="activeThreadId"
          @new-chat="emit('new-chat'); mobileSheetOpen = false"
          @select-thread="emit('select-thread', $event); mobileSheetOpen = false"
          @delete-thread="emit('delete-thread', $event)"
        >
          <template v-if="$slots['sidebar-header']" #header>
            <slot name="sidebar-header" />
          </template>
          <template v-if="$slots['sidebar-footer']" #footer>
            <slot name="sidebar-footer" />
          </template>
        </ChatSidebar>
      </NDrawerContent>
    </NDrawer>

    <!-- Desktop sidebar -->
    <div v-if="sidebarOpen" class="t-chat-layout__sidebar hidden md:block">
      <ChatSidebar
        :threads="threads"
        :active-thread-id="activeThreadId"
        @new-chat="emit('new-chat')"
        @select-thread="emit('select-thread', $event)"
        @delete-thread="emit('delete-thread', $event)"
      >
        <template v-if="$slots['sidebar-header']" #header>
          <slot name="sidebar-header" />
        </template>
        <template v-if="$slots['sidebar-footer']" #footer>
          <slot name="sidebar-footer" />
        </template>
      </ChatSidebar>
    </div>

    <!-- Main content -->
    <div class="t-chat-layout__main">
      <ChatMain
        :messages="messages"
        :is-streaming="isStreaming"
        :input-text="inputText"
        :suggestions="suggestions"
        :agent-name="agentName"
        :thread-title="threadTitle"
        @send="(content: string, files: File[]) => emit('send', content, files)"
        @stop="emit('stop')"
        @copy="emit('copy', $event)"
        @regenerate="emit('regenerate', $event)"
        @edit="emit('edit', $event)"
        @feedback="(id, type, reason) => emit('feedback', id, type, reason)"
        @update:input-text="emit('update:inputText', $event)"
        @export="emit('export')"
        @settings="settingsOpen = !settingsOpen"
        @toggle-sidebar="sidebarOpen = !sidebarOpen; mobileSheetOpen = !mobileSheetOpen"
      >
        <template v-if="$slots['header-extra']" #header-extra>
          <slot name="header-extra" />
        </template>
        <template v-if="$slots['input-above']" #input-above>
          <slot name="input-above" />
        </template>
      </ChatMain>
    </div>

    <!-- Artifact panel -->
    <div v-if="artifact" class="t-chat-layout__artifact">
      <ChatArtifactPanel
        :title="artifact.title"
        :code="artifact.code"
        :language="artifact.language"
        :preview-url="artifact.previewUrl"
        :preview-html="artifact.previewHtml"
        @close="emit('close-artifact')"
        @download="emit('download-artifact')"
      />
    </div>

    <!-- Settings panel -->
    <div v-if="settingsOpen && settings" class="t-chat-layout__settings">
      <ChatSettings
        :settings="settings"
        :available-models="availableModels"
        @update:settings="emit('update:settings', $event)"
        @close="settingsOpen = false"
      />
    </div>
  </div>
</template>

<style scoped>
.t-chat-layout {
  display: flex;
  height: 100%;
}
.t-chat-layout__sidebar {
  width: min(280px, 25vw);
  flex-shrink: 0;
  border-right: 1px solid var(--tnzi-border);
  overflow: hidden;
}
.t-chat-layout__main {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.t-chat-layout__artifact {
  width: min(400px, 30vw);
  flex-shrink: 0;
  border-left: 1px solid var(--tnzi-border);
  overflow: hidden;
}
.t-chat-layout__settings {
  width: min(280px, 22vw);
  flex-shrink: 0;
  border-left: 1px solid var(--tnzi-border);
  overflow: hidden;
}
</style>
