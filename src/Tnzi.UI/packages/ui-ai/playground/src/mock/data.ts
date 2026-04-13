import type { ChatMessage } from '@tnzi/ui-ai/composables';

// ---- Threads ----
export interface MockThread {
  id: string;
  title: string;
  lastMessage: string;
  updatedAt: string;
}

export const mockThreads: MockThread[] = [
  { id: 'thread-1', title: 'JavaScript async/await', lastMessage: 'Sure! async/await is syntactic sugar...', updatedAt: new Date(Date.now() - 3600000).toISOString() },
  { id: 'thread-2', title: 'Python data analysis', lastMessage: 'You can use pandas to...', updatedAt: new Date(Date.now() - 7200000).toISOString() },
  { id: 'thread-3', title: 'Database optimization', lastMessage: 'Consider adding indexes on...', updatedAt: new Date(Date.now() - 86400000).toISOString() },
  { id: 'thread-4', title: 'React vs Vue comparison', lastMessage: 'Both frameworks are excellent...', updatedAt: new Date(Date.now() - 172800000).toISOString() },
  { id: 'thread-5', title: 'Docker deployment guide', lastMessage: 'First, create a Dockerfile...', updatedAt: new Date(Date.now() - 259200000).toISOString() },
];

// ---- Chat Messages (rich with reasoning + tool calls) ----
export const mockMessages: ChatMessage[] = [
  {
    id: '1',
    role: 'user',
    content: 'Can you explain how async/await works in JavaScript?',
    createdAt: new Date(Date.now() - 120000).toISOString(),
  },
  {
    id: '2',
    role: 'assistant',
    content: '',
    createdAt: new Date(Date.now() - 115000).toISOString(),
    reasoning: 'The user wants to understand async/await. Let me start with a simple explanation, then show a practical example with error handling.',
    toolCalls: [
      { name: 'web_search', durationMs: 820, output: 'Found MDN documentation on async/await' },
    ],
  },
  {
    id: '3',
    role: 'assistant',
    content: `## Async/Await in JavaScript

**async/await** is syntactic sugar over Promises that makes asynchronous code look and behave like synchronous code.

### The Basics

1. **\`async\` function** — always returns a Promise
2. **\`await\` keyword** — pauses execution until the Promise resolves

### Simple Example

\`\`\`javascript
async function fetchUserData(userId) {
  const response = await fetch(\`/api/users/\${userId}\`);
  const data = await response.json();
  return data;
}
\`\`\`

### Error Handling

\`\`\`javascript
async function safeFetch(url) {
  try {
    const response = await fetch(url);
    if (!response.ok) throw new Error(\`HTTP \${response.status}\`);
    return await response.json();
  } catch (error) {
    console.error('Fetch failed:', error.message);
    return null;
  }
}
\`\`\`

### Comparison Table

| Pattern | Pros | Cons |
|---------|------|------|
| Callbacks | Simple | Callback hell |
| Promises | Chainable | Verbose |
| async/await | Readable | Needs transpiler for old browsers |

> **Key insight**: \`await\` only pauses the current function — the event loop continues processing other tasks.`,
    createdAt: new Date(Date.now() - 110000).toISOString(),
  },
  {
    id: '4',
    role: 'user',
    content: 'What about error handling best practices?',
    createdAt: new Date(Date.now() - 60000).toISOString(),
  },
  {
    id: '5',
    role: 'assistant',
    content: `## Real-World Error Handling

Here's a production-ready **Result wrapper** pattern:

\`\`\`typescript
type Result<T> = { ok: true; data: T } | { ok: false; error: string };

async function safeAsync<T>(fn: () => Promise<T>): Promise<Result<T>> {
  try {
    const data = await fn();
    return { ok: true, data };
  } catch (e) {
    return { ok: false, error: e instanceof Error ? e.message : String(e) };
  }
}
\`\`\`

The math formula for retry delay: $t = b^n$ where $b$ is the base delay and $n$ is the attempt number.

This gives you **exponential backoff**: $$T_{total} = \\sum_{i=0}^{n} b^i$$`,
    createdAt: new Date(Date.now() - 55000).toISOString(),
    reasoning: 'Instead of basic try/catch, I should show the Result wrapper pattern used in production.',
  },
];

// ---- Streaming simulation responses ----
export const streamingResponses: string[] = [
  `Great question! Let me think about this...\n\n### Key Points\n\n1. **First**, consider the context\n2. **Second**, a practical example\n3. **Third**, best practices\n\n\`\`\`python\ndef hello_world():\n    print("Hello from AI!")\n    return {"status": "success"}\n\nhello_world()\n\`\`\`\n\n> This is a simulated streaming response.\n\nWould you like to explore further?`,
  `I'd be happy to help!\n\n## Analysis\n\n| Step | Action | Result |\n|------|--------|--------|\n| 1 | Research | Found data |\n| 2 | Analyze | Identified patterns |\n| 3 | Synthesize | Created recommendations |\n\n### Implementation\n\n\`\`\`typescript\ninterface Config {\n  apiKey: string;\n  timeout: number;\n}\n\nfunction createClient(config: Config) {\n  return {\n    async query(prompt: string) {\n      const res = await fetch('/api', {\n        method: 'POST',\n        body: JSON.stringify({ prompt }),\n      });\n      return res.json();\n    }\n  };\n}\n\`\`\`\n\nLet me know if you need more details!`,
];

// ---- Suggestions ----
export const mockSuggestions = [
  { text: 'Explain quantum computing', icon: 'lucide:atom' },
  { text: 'Write a Python script', icon: 'lucide:code-2' },
  { text: 'Compare React vs Vue', icon: 'lucide:git-compare' },
  { text: 'Help me with SQL queries', icon: 'lucide:database' },
];

// ---- Models ----
export const mockModels = [
  { id: 'gpt-4o', name: 'GPT-4o', provider: 'openai', group: 'OpenAI' },
  { id: 'gpt-4o-mini', name: 'GPT-4o Mini', provider: 'openai', group: 'OpenAI' },
  { id: 'claude-sonnet-4-6', name: 'Claude Sonnet 4.6', provider: 'anthropic', group: 'Anthropic' },
  { id: 'claude-opus-4-6', name: 'Claude Opus 4.6', provider: 'anthropic', group: 'Anthropic' },
  { id: 'deepseek-r1', name: 'DeepSeek R1', provider: 'deepseek', group: 'DeepSeek' },
  { id: 'gemini-2.5-pro', name: 'Gemini 2.5 Pro', provider: 'google', group: 'Google' },
];

// ---- Agents ----
export const mockAgents = [
  { id: 'a1', name: 'Research Agent', description: 'Searches the web and summarizes findings', icon: 'lucide:search', tags: ['research', 'web'] },
  { id: 'a2', name: 'Code Assistant', description: 'Writes, reviews, and debugs code', icon: 'lucide:code-2', tags: ['coding', 'debug'] },
  { id: 'a3', name: 'Data Analyst', description: 'Analyzes data and creates visualizations', icon: 'lucide:bar-chart-3', tags: ['data', 'analytics'] },
  { id: 'a4', name: 'Writing Coach', description: 'Helps improve writing quality and clarity', icon: 'lucide:pen-tool', tags: ['writing', 'editing'] },
  { id: 'a5', name: 'DevOps Engineer', description: 'Manages infrastructure and deployments', icon: 'lucide:server', tags: ['devops', 'cloud'] },
  { id: 'a6', name: 'Security Auditor', description: 'Reviews code for security vulnerabilities', icon: 'lucide:shield', tags: ['security', 'audit'] },
];

// ---- Personas ----
export const mockPersonas = [
  { id: 'p1', name: 'Helpful Assistant', slug: 'helpful', description: 'Friendly and helpful general assistant' },
  { id: 'p2', name: 'Senior Developer', slug: 'senior-dev', description: 'Expert-level technical guidance' },
  { id: 'p3', name: 'Creative Writer', slug: 'creative', description: 'Imaginative and expressive writing style' },
];

// ---- Skills ----
export const mockSkills = [
  { id: 's1', slug: 'web-search', name: 'Web Search', description: 'Search the internet for real-time information', icon: 'lucide:globe', category: 'Research', isActive: true, isBuiltIn: true },
  { id: 's2', slug: 'code-exec', name: 'Code Execution', description: 'Execute Python code in a sandbox', icon: 'lucide:play', category: 'Coding', isActive: true, isBuiltIn: true },
  { id: 's3', slug: 'image-gen', name: 'Image Generation', description: 'Generate images from text prompts', icon: 'lucide:image', category: 'Creative', isActive: false, isBuiltIn: true },
  { id: 's4', slug: 'pdf-reader', name: 'PDF Reader', description: 'Read and extract content from PDF files', icon: 'lucide:file-text', category: 'Research', isActive: false, isBuiltIn: false },
  { id: 's5', slug: 'sql-query', name: 'SQL Query', description: 'Execute SQL queries on connected databases', icon: 'lucide:database', category: 'Data', isActive: true, isBuiltIn: false },
  { id: 's6', slug: 'email-send', name: 'Email Sender', description: 'Send emails via configured SMTP', icon: 'lucide:mail', category: 'Communication', isActive: false, isBuiltIn: false },
];

// ---- Knowledge Bases ----
export const mockKnowledgeBases = [
  { id: 'kb1', name: 'Company Docs', description: 'Internal documentation and policies', documentCount: 156, icon: 'lucide:building' },
  { id: 'kb2', name: 'Product Manual', description: 'User guides and API documentation', documentCount: 42 },
  { id: 'kb3', name: 'Research Papers', description: 'Academic papers and technical reports', documentCount: 89, icon: 'lucide:graduation-cap' },
];

// ---- Citations ----
export const mockCitations = [
  { title: 'MDN: async/await', url: 'https://developer.mozilla.org/en-US/docs/Learn/JavaScript/Asynchronous/Promises' },
  { title: 'JavaScript.info: Error handling', url: 'https://javascript.info/try-catch' },
  { title: 'TypeScript Handbook: Utility Types', url: 'https://www.typescriptlang.org/docs/handbook/utility-types.html' },
];

// ---- MCP Tools ----
export const mockMcpTools = [
  { name: 'file_read', description: 'Read file contents from the filesystem', status: 'available' as const },
  { name: 'file_write', description: 'Write content to a file', status: 'available' as const },
  { name: 'shell_exec', description: 'Execute shell commands', status: 'available' as const },
  { name: 'browser_navigate', description: 'Navigate to a URL in the browser', status: 'error' as const },
  { name: 'db_query', description: 'Execute database queries', status: 'available' as const },
];

// ---- Workflow ----
export const mockWorkflowNodes = [
  { id: 'n1', type: 'input', label: 'User Query', position: { x: 0, y: 100 }, status: 'completed' as const },
  { id: 'n2', type: 'process', label: 'Web Search', description: 'Search for information', position: { x: 250, y: 0 }, status: 'completed' as const },
  { id: 'n3', type: 'process', label: 'Summarize', description: 'Summarize search results', position: { x: 250, y: 200 }, status: 'active' as const },
  { id: 'n4', type: 'process', label: 'Generate Response', position: { x: 500, y: 100 }, status: 'pending' as const },
  { id: 'n5', type: 'output', label: 'Final Answer', position: { x: 750, y: 100 }, status: 'pending' as const },
];

export const mockWorkflowEdges = [
  { id: 'e1', source: 'n1', target: 'n2' },
  { id: 'e2', source: 'n1', target: 'n3' },
  { id: 'e3', source: 'n2', target: 'n4', animated: true },
  { id: 'e4', source: 'n3', target: 'n4', animated: true },
  { id: 'e5', source: 'n4', target: 'n5' },
];

// ---- Queue/Plan ----
export const mockQueueSections = [
  {
    label: 'In Progress',
    icon: 'lucide:loader-2',
    items: [
      { id: 'q1', title: 'Search for async/await best practices', completed: false },
      { id: 'q2', title: 'Analyze search results', completed: false, description: 'Extract key patterns and examples' },
    ],
  },
  {
    label: 'Completed',
    icon: 'lucide:check-circle',
    items: [
      { id: 'q3', title: 'Parse user query intent', completed: true },
      { id: 'q4', title: 'Identify relevant topics', completed: true, files: ['topics.json'] },
    ],
  },
];

// ---- Admin mock data ----
export const mockAdminAgents = [
  { id: 'a1', name: 'Research Agent', description: 'Web search and summarization', version: 3, createdAt: new Date().toISOString() },
  { id: 'a2', name: 'Code Assistant', description: 'Code generation and review', version: 5, createdAt: new Date().toISOString() },
  { id: 'a3', name: 'Data Analyst', description: 'Data analysis and visualization', version: 2, createdAt: new Date().toISOString() },
];

export const mockUsageSummary = {
  totalRequests: 45230,
  totalTokens: 12500000,
  totalCost: 342.50,
  activeAgents: 8,
};

export const mockUsageLogs = [
  { agent: 'Research Agent', date: '2026-03-29', requests: 1250, tokens: 450000, cost: 12.50 },
  { agent: 'Code Assistant', date: '2026-03-29', requests: 890, tokens: 320000, cost: 8.90 },
  { agent: 'Data Analyst', date: '2026-03-29', requests: 560, tokens: 180000, cost: 5.60 },
];
