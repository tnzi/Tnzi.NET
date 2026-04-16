import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '01-simple-chat',
    title: 'Simple Q&A',
    description: 'Basic conversational exchange with streaming deltas',
    category: 'conversation',
    icon: 'lucide:message-circle',
    componentsShowcased: ['ChatBox', 'MessageList', 'StreamingText', 'PromptInput', 'MessageActions'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'What is TypeScript?' },
    { at: 500, type: 'assistant-start', agentName: 'Tnzi Assistant', model: 'gpt-4' },
    { at: 700, type: 'assistant-delta', text: 'TypeScript is a ' },
    { at: 900, type: 'assistant-delta', text: 'strongly-typed superset ' },
    { at: 1100, type: 'assistant-delta', text: 'of JavaScript that adds ' },
    { at: 1300, type: 'assistant-delta', text: 'static type checking, interfaces, ' },
    { at: 1500, type: 'assistant-delta', text: 'generics, and advanced type inference.' },
    { at: 1700, type: 'assistant-end', usage: { promptTokens: 12, completionTokens: 26, totalTokens: 38 } },
    { at: 3000, type: 'user-message', content: 'Show me a quick example.' },
    { at: 3400, type: 'assistant-start' },
    { at: 3600, type: 'assistant-delta', text: "Here's a simple typed function:\n\n" },
    { at: 3900, type: 'assistant-delta', text: '```typescript\n' },
    { at: 4200, type: 'assistant-delta', text: 'function greet(name: string): string {\n' },
    { at: 4500, type: 'assistant-delta', text: '  return `Hello, ${name}!`;\n' },
    { at: 4800, type: 'assistant-delta', text: '}\n```\n' },
    { at: 5100, type: 'assistant-delta', text: '\nThe `: string` annotations enforce both parameter and return types at compile time.' },
    { at: 5400, type: 'assistant-end', usage: { promptTokens: 50, completionTokens: 48, totalTokens: 98 } },
  ],
}

export default scenario
