<script setup lang="ts">
import { ref } from 'vue';
import { ChatLayout } from '@tnzi/ai/chat';
import type { ChatMessage } from '@tnzi/ai/composables';
import { mockThreads, mockMessages, mockSuggestions, streamingResponses } from '../mock/data';

const threads = ref(mockThreads.map(t => ({ ...t })));
const activeThreadId = ref('thread-1');
const messages = ref<ChatMessage[]>([...mockMessages]);
const inputText = ref('');
const isStreaming = ref(false);
const artifact = ref<{ title: string; code: string; language: string; previewHtml?: string } | null>(null);

let abortController: AbortController | null = null;
let responseIndex = 0;

function handleSend(content: string, _files: File[]) {
  if (!content.trim()) return;
  messages.value = [...messages.value, { id: `msg_${Date.now()}`, role: 'user', content, createdAt: new Date().toISOString() }];
  isStreaming.value = true;
  abortController = new AbortController();
  const assistantId = `msg_${Date.now()}_reply`;
  const fullResponse = streamingResponses[responseIndex % streamingResponses.length];
  responseIndex++;
  messages.value = [...messages.value, { id: assistantId, role: 'assistant', content: '', createdAt: new Date().toISOString(), isStreaming: true, reasoning: content.length > 20 ? 'Let me think about this carefully...' : undefined }];
  let charIndex = 0;
  const interval = setInterval(() => {
    if (abortController?.signal.aborted) { clearInterval(interval); finishStreaming(assistantId); return; }
    const chunkSize = 3 + Math.floor(Math.random() * 6);
    const nextChars = fullResponse.slice(charIndex, charIndex + chunkSize);
    charIndex += chunkSize;
    messages.value = messages.value.map(m => m.id === assistantId ? { ...m, content: m.content + nextChars } : m);
    if (charIndex >= fullResponse.length) { clearInterval(interval); finishStreaming(assistantId); }
  }, 30);
}

function finishStreaming(id: string) {
  messages.value = messages.value.map(m => m.id === id ? { ...m, isStreaming: false } : m);
  isStreaming.value = false;
  abortController = null;
}

function toggleArtifact() {
  artifact.value = artifact.value ? null : {
    title: 'example.ts',
    code: `interface Config {\n  apiKey: string;\n  timeout: number;\n}\n\nfunction createClient(config: Config) {\n  return {\n    async query(prompt: string) {\n      const res = await fetch('/api');\n      return res.json();\n    }\n  };\n}`,
    language: 'typescript',
    previewHtml: '<html><body style="font-family:system-ui;padding:2rem"><h1>Preview</h1><p>Artifact preview content</p></body></html>',
  };
}
</script>

<template>
  <div class="h-full">
    <ChatLayout
      :threads="threads"
      :active-thread-id="activeThreadId"
      :messages="messages"
      :is-streaming="isStreaming"
      :input-text="inputText"
      :suggestions="messages.length <= 5 ? mockSuggestions : []"
      :artifact="artifact"
      @new-chat="messages = []; activeThreadId = `t_${Date.now()}`"
      @select-thread="activeThreadId = $event"
      @delete-thread="threads = threads.filter(t => t.id !== $event)"
      @send="handleSend"
      @stop="abortController?.abort()"
      @update:input-text="inputText = $event"
      @close-artifact="artifact = null"
    >
      <template #sidebar-header>
        <button class="w-full rounded-md bg-primary px-3 py-2 text-sm text-primary-foreground hover:bg-primary/90" @click="toggleArtifact">
          {{ artifact ? 'Hide Artifact' : 'Show Artifact' }}
        </button>
      </template>
    </ChatLayout>
  </div>
</template>
