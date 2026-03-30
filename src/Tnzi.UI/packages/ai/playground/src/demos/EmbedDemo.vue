<script setup lang="ts">
import { ref } from 'vue';
import { FloatingChat, SidebarChat, InlineChat } from '@tnzi/ai/embed';
import type { ChatMessage } from '@tnzi/ai/composables';
import { mockMessages } from '../mock/data';

const mode = ref<'floating' | 'sidebar' | 'inline'>('floating');
const messages = ref<ChatMessage[]>(mockMessages.slice(0, 3));
const inputText = ref('');
</script>

<template>
  <div class="h-full flex flex-col">
    <div class="border-b p-4 flex gap-2 shrink-0">
      <button v-for="m in ['floating', 'sidebar', 'inline'] as const" :key="m"
        class="rounded-md px-3 py-1.5 text-sm transition-colors"
        :class="mode === m ? 'bg-primary text-primary-foreground' : 'bg-muted hover:bg-accent'"
        @click="mode = m">
        {{ m.charAt(0).toUpperCase() + m.slice(1) }}
      </button>
    </div>
    <div class="flex-1 relative bg-muted/30 overflow-hidden">
      <!-- Simulated host page -->
      <div class="p-8 space-y-4">
        <h1 class="text-2xl font-bold">Host Application</h1>
        <p class="text-muted-foreground">This simulates a host page with an embedded AI chat widget.</p>
        <div class="h-40 rounded-lg border-2 border-dashed border-border flex items-center justify-center text-muted-foreground">Content area</div>
      </div>

      <!-- Embed mode -->
      <FloatingChat v-if="mode === 'floating'" :messages="messages" :input-text="inputText" @update:input-text="inputText = $event" />
      <SidebarChat v-if="mode === 'sidebar'" :messages="messages" :input-text="inputText" @update:input-text="inputText = $event" />
      <div v-if="mode === 'inline'" class="absolute inset-x-8 bottom-8 h-[300px] rounded-lg border shadow-lg overflow-hidden">
        <InlineChat :messages="messages" :input-text="inputText" @update:input-text="inputText = $event" />
      </div>
    </div>
  </div>
</template>
