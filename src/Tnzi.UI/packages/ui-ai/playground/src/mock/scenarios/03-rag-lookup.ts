import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '03-rag-lookup',
    title: 'RAG Knowledge Base',
    description: 'Answer grounded in knowledge base citations',
    category: 'knowledge',
    icon: 'lucide:book-open',
    componentsShowcased: ['KnowledgeBaseCard', 'CitationList', 'useRagChat'],
  },
  events: [
    { at: 0, type: 'user-message', content: 'What modules does Tnzi.NET ship?' },
    { at: 500, type: 'assistant-start', agentName: 'KB Agent' },
    {
      at: 700,
      type: 'citation',
      source: {
        id: 'c1',
        title: 'Tnzi.NET Architecture — Module System',
        snippet: 'Tnzi.NET organizes functionality into core, infrastructure, framework, application, and custom module tiers.',
        url: 'docs/architecture.md#module-system',
        pageNumber: 12,
      },
    },
    { at: 900, type: 'assistant-delta', text: 'Tnzi.NET organizes functionality across five module tiers: ' },
    { at: 1200, type: 'assistant-delta', text: '**core**, **infrastructure**, **framework**, **application**, and **custom**.\n\n' },
    {
      at: 1500,
      type: 'citation',
      source: {
        id: 'c2',
        title: 'Module Catalog',
        snippet: 'Application modules include Identity, Authorization, Storage, Chat, Notification, Template, Audit, AI, and AI sub-modules.',
        url: 'docs/modules/index.md',
      },
    },
    { at: 1800, type: 'assistant-delta', text: 'Application modules include Identity, Authorization, Storage, ' },
    { at: 2100, type: 'assistant-delta', text: 'Chat, Notification, Template, Audit, and the AI stack (with sub-modules for Skills, Workflow, MCP, Sandbox, and Channels).' },
    { at: 2400, type: 'assistant-end', usage: { promptTokens: 28, completionTokens: 62, totalTokens: 90 } },
  ],
}

export default scenario
