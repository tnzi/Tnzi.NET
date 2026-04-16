import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '02-deep-reasoning',
    title: 'Deep Reasoning',
    description: 'Shows reasoning trace alongside the final answer',
    category: 'reasoning',
    icon: 'lucide:brain',
    componentsShowcased: ['ReasoningTrace', 'ChainOfThought', 'StreamingText', 'TypingIndicator'],
  },
  events: [
    {
      at: 0,
      type: 'user-message',
      content: 'A farmer has chickens and cows totaling 20 heads and 56 legs. How many of each?',
    },
    { at: 400, type: 'assistant-start', model: 'o1-preview' },
    { at: 600, type: 'assistant-reasoning-delta', text: 'Let me set up variables. ' },
    { at: 900, type: 'assistant-reasoning-delta', text: 'Let c = chickens, k = cows. ' },
    { at: 1200, type: 'assistant-reasoning-delta', text: 'Heads equation: c + k = 20. ' },
    { at: 1500, type: 'assistant-reasoning-delta', text: 'Legs equation: 2c + 4k = 56. ' },
    { at: 1800, type: 'assistant-reasoning-delta', text: 'Substitute c = 20 - k into the second: ' },
    { at: 2100, type: 'assistant-reasoning-delta', text: '2(20 - k) + 4k = 56 → 40 + 2k = 56 → k = 8. ' },
    { at: 2400, type: 'assistant-reasoning-delta', text: 'Therefore c = 12.' },
    { at: 2700, type: 'assistant-delta', text: 'The farmer has **12 chickens** and **8 cows**.\n\n' },
    { at: 3000, type: 'assistant-delta', text: 'Verification: 12 + 8 = 20 heads ✓; 2·12 + 4·8 = 24 + 32 = 56 legs ✓.' },
    { at: 3300, type: 'assistant-end', usage: { promptTokens: 32, completionTokens: 85, totalTokens: 117 } },
  ],
}

export default scenario
