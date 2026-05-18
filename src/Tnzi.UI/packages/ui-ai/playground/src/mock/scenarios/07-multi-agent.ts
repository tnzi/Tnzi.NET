import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '07-multi-agent',
    title: 'Multi-Agent Handoff',
    description: 'Three agents collaborate on a product research task',
    category: 'agent',
    icon: 'lucide:users',
    componentsShowcased: ['AgentStatus', 'AgentQueue', 'AgentHandoff', 'ChatMessage', 'MessageBranch'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'Give me a quick market snapshot for standing desks.' },
    { at: 400, type: 'assistant-start', agentName: 'Researcher' },
    { at: 600, type: 'assistant-delta', text: 'Scanning public sources…\n\n' },
    { at: 900, type: 'assistant-delta', text: '- Global market ~$5B in 2024, projected ~$9B by 2030\n' },
    { at: 1200, type: 'assistant-delta', text: '- Leading brands: Uplift, Fully, Flexispot, IKEA Bekant\n' },
    { at: 1500, type: 'assistant-delta', text: '- Price range: $200 (entry) → $1500 (premium frame + top)\n' },
    { at: 1800, type: 'assistant-end', usage: { promptTokens: 20, completionTokens: 55, totalTokens: 75 } },
    { at: 2000, type: 'assistant-start', agentName: 'Analyst' },
    { at: 2200, type: 'assistant-delta', text: 'Key trends I see:\n\n' },
    { at: 2500, type: 'assistant-delta', text: '1. **Hybrid work** post-pandemic normalized home office investment\n' },
    { at: 2800, type: 'assistant-delta', text: '2. **Health messaging** (posture, "sitting is the new smoking") drives higher-end adoption\n' },
    { at: 3100, type: 'assistant-delta', text: '3. **Commoditization at $300-500** squeezing mid-tier brand margins\n' },
    { at: 3400, type: 'assistant-end', usage: { promptTokens: 80, completionTokens: 70, totalTokens: 150 } },
    { at: 3600, type: 'assistant-start', agentName: 'Writer' },
    { at: 3800, type: 'assistant-delta', text: '**Summary:** Standing desks are in a maturing ~$5B market with hybrid-work tailwinds. ' },
    { at: 4100, type: 'assistant-delta', text: 'Premium brands (Uplift, Fully) differentiate on motor quality and ergonomic features; ' },
    { at: 4400, type: 'assistant-delta', text: 'budget entrants at $300-500 pressure mid-tier margins. Health/posture framing remains the primary purchase driver.' },
    { at: 4700, type: 'assistant-end', usage: { promptTokens: 150, completionTokens: 85, totalTokens: 235 } },
  ],
}

export default scenario
