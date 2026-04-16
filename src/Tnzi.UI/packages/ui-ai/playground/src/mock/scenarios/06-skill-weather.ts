import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '06-skill-weather',
    title: 'Skill: Weather Lookup',
    description: 'Invokes a registered skill, shows tool-call and tool-result bubbles',
    category: 'agent',
    icon: 'lucide:cloud-sun',
    componentsShowcased: ['SkillBrowserPanel', 'SkillCard', 'MessageResponse'],
  },
  events: [
    { at: 0, type: 'user-message', content: "What's the weather in Beijing today?" },
    { at: 400, type: 'assistant-start' },
    { at: 600, type: 'assistant-delta', text: 'Let me check the weather skill.' },
    {
      at: 900,
      type: 'tool-call',
      name: 'weather-api',
      input: { city: 'Beijing', units: 'metric', lang: 'en' },
    },
    {
      at: 1600,
      type: 'tool-result',
      name: 'weather-api',
      output: {
        city: 'Beijing',
        condition: 'Partly Cloudy',
        temperatureC: 14,
        humidity: 42,
        windKmh: 12,
      },
    },
    { at: 1900, type: 'assistant-delta', text: '\n\nBeijing is **partly cloudy** right now, ' },
    { at: 2200, type: 'assistant-delta', text: 'around **14°C** with 42% humidity and a 12 km/h breeze. ' },
    { at: 2500, type: 'assistant-delta', text: 'Comfortable for outdoor activities — consider a light jacket for the evening.' },
    { at: 2800, type: 'assistant-end', usage: { promptTokens: 15, completionTokens: 72, totalTokens: 87 } },
  ],
}

export default scenario
