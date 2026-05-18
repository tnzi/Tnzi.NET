import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '09-message-branch',
    title: 'Message Branching',
    description: 'Edit an earlier user message and compare regenerated replies',
    category: 'conversation',
    icon: 'lucide:git-fork',
    componentsShowcased: ['MessageBranch', 'MessageActions', 'useMessageBranch'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'Write a haiku about autumn.' },
    { at: 400, type: 'assistant-start' },
    { at: 600, type: 'assistant-delta', text: 'Crimson leaves drifting —\n' },
    { at: 900, type: 'assistant-delta', text: 'a crow calls across the field,\n' },
    { at: 1200, type: 'assistant-delta', text: 'dusk swallows the hill.' },
    { at: 1500, type: 'assistant-end', usage: { promptTokens: 8, completionTokens: 22, totalTokens: 30 } },
    { at: 2800, type: 'user-message', content: 'Write a haiku about autumn. (edited: add a reference to rain.)' },
    { at: 3200, type: 'assistant-start' },
    { at: 3400, type: 'assistant-delta', text: 'Cold rain on maples —\n' },
    { at: 3700, type: 'assistant-delta', text: 'each drop a small bronze bell,\n' },
    { at: 4000, type: 'assistant-delta', text: "autumn's quiet hymn." },
    { at: 4300, type: 'assistant-end', usage: { promptTokens: 14, completionTokens: 26, totalTokens: 40 } },
  ],
}

export default scenario
