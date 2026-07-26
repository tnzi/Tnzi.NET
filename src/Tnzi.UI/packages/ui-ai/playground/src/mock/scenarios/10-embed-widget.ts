import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '10-embed-widget',
    title: 'Embed Widget',
    description: 'Demonstrates the floating / sidebar / inline embed mode switcher',
    category: 'embed',
    icon: 'lucide:box',
    componentsShowcased: ['FloatingChat', 'SidebarChat', 'useEmbedMode'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'How do I embed this chat on my site?' },
    { at: 400, type: 'assistant-start' },
    { at: 600, type: 'assistant-delta', text: 'There are three embed modes:\n\n' },
    { at: 900, type: 'assistant-delta', text: '- **Floating** - bottom-right bubble that expands into a chat card\n' },
    { at: 1200, type: 'assistant-delta', text: '- **Sidebar** - docked side drawer for persistent access\n' },
    { at: 1500, type: 'assistant-delta', text: '- **Inline** - full component occupying a page section\n\n' },
    { at: 1800, type: 'assistant-delta', text: 'Try the mode switcher in the playground corner to see each one.' },
    { at: 2100, type: 'assistant-end', usage: { promptTokens: 20, completionTokens: 55, totalTokens: 75 } },
  ],
}

export default scenario
