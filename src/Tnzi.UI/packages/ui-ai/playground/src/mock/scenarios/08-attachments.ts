import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '08-attachments',
    title: 'File Attachments',
    description: 'Image and PDF uploads in the user message',
    category: 'artifact',
    icon: 'lucide:paperclip',
    componentsShowcased: ['MessageAttachments', 'PromptInput'],
  },
  events: [
    {
      at: 0,
      type: 'user-message',
      content: 'Can you analyze this chart and summarize the trend?',
      attachments: [
        {
          id: 'a1',
          name: 'q3-revenue.png',
          mime: 'image/png',
          sizeBytes: 142_003,
          previewUrl: 'https://placehold.co/600x400/png?text=Q3+Revenue+Chart',
        },
      ],
    },
    { at: 500, type: 'assistant-start' },
    { at: 800, type: 'assistant-delta', text: 'Looking at the chart, I can see revenue ' },
    { at: 1100, type: 'assistant-delta', text: 'climbed steadily from Q1 through Q3, with a notable acceleration in Q3. ' },
    { at: 1400, type: 'assistant-delta', text: 'The quarter-over-quarter growth went from ~8% to ~22%, suggesting a product launch or enterprise deal cycle closed during that window.' },
    { at: 1700, type: 'assistant-end', usage: { promptTokens: 40, completionTokens: 80, totalTokens: 120 } },
    {
      at: 3000,
      type: 'user-message',
      content: "Here's the Q4 deck — pull out the three highest-risk items.",
      attachments: [
        {
          id: 'a2',
          name: 'Q4-board-deck.pdf',
          mime: 'application/pdf',
          sizeBytes: 2_480_112,
        },
      ],
    },
    { at: 3500, type: 'assistant-start' },
    { at: 3800, type: 'assistant-delta', text: 'Top three risks flagged in the deck:\n\n' },
    { at: 4100, type: 'assistant-delta', text: '1. **Key account concentration** — top 3 customers = 47% of ARR\n' },
    { at: 4400, type: 'assistant-delta', text: '2. **Hiring gap in engineering** — 6 open senior roles against Q4 commitments\n' },
    { at: 4700, type: 'assistant-delta', text: '3. **Vendor migration dependency** — two critical services sunsetting before March' },
    { at: 5000, type: 'assistant-end', usage: { promptTokens: 120, completionTokens: 95, totalTokens: 215 } },
  ],
}

export default scenario
