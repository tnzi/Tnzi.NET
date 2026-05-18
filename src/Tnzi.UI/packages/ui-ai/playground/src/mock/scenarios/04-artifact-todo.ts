import type { MockScenario } from '../types'

const todoCode = `<script setup lang="ts">
import { ref } from 'vue'

interface Todo { id: number; text: string; done: boolean }

const todos = ref<Todo[]>([])
const input = ref('')

function add(): void {
  if (!input.value.trim()) return
  todos.value.push({ id: Date.now(), text: input.value, done: false })
  input.value = ''
}

function toggle(id: number): void {
  const t = todos.value.find((x) => x.id === id)
  if (t) t.done = !t.done
}
</script>

<template>
  <div class="todo">
    <input v-model="input" @keyup.enter="add" placeholder="Add a todo…" />
    <ul>
      <li v-for="t in todos" :key="t.id" :class="{ done: t.done }" @click="toggle(t.id)">
        {{ t.text }}
      </li>
    </ul>
  </div>
</template>
`

const scenario: MockScenario = {
  meta: {
    id: '04-artifact-todo',
    title: 'Artifact: Vue Todo',
    description: 'Generates a code artifact shown in the side panel',
    category: 'artifact',
    icon: 'lucide:file-code-2',
    componentsShowcased: ['ArtifactPreview', 'TArtifactPanel', 'shiki'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'Write a Vue 3 todo list component with TypeScript.' },
    { at: 500, type: 'assistant-start' },
    { at: 700, type: 'assistant-delta', text: "I'll create a minimal Vue 3 todo component using the Composition API with `<script setup>`. " },
    { at: 1000, type: 'assistant-delta', text: 'Opening the artifact panel on the right…\n' },
    {
      at: 1400,
      type: 'artifact',
      artifact: {
        id: 'art-todo',
        title: 'Todo.vue',
        kind: 'code',
        language: 'vue',
        content: todoCode,
      },
    },
    { at: 1800, type: 'assistant-delta', text: '\nThe component tracks a list of todos with id/text/done, supports adding on Enter, ' },
    { at: 2100, type: 'assistant-delta', text: 'and toggles completion on click. Styles are omitted — add a `.done { text-decoration: line-through }` rule to show completion visually.' },
    { at: 2400, type: 'assistant-end', usage: { promptTokens: 22, completionTokens: 210, totalTokens: 232 } },
  ],
}

export default scenario
