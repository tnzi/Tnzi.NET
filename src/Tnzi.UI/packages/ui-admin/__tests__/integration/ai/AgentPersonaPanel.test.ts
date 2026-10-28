/**
 * The persona panel offers Save only to a viewer who may save.
 *
 * The page skips a persona save without `ai.agent.update` (no request, no
 * message), so an editable box with a Save button let a reader type, click
 * Save and see nothing happen.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AgentPersonaPanel from '../../../src/pages/ai/agents/sections/AgentPersonaPanel.vue'

const stubs = {
  TSvgIcon: true,
  Input: { name: 'Input', props: ['value', 'readonly'], template: '<textarea class="persona-input" :readonly="readonly" :value="value" />' },
}

describe('AgentPersonaPanel', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('is an editor with Save for a viewer who may update the agent', () => {
    const wrapper = mount(AgentPersonaPanel, { props: { persona: 'You are terse.' }, global: { stubs } })
    expect(wrapper.find('.t-persona__actions').exists()).toBe(true)
    expect(wrapper.find('.persona-input').attributes('readonly')).toBeUndefined()
  })

  it('is read-only with no Save without ai.agent.update', () => {
    const wrapper = mount(AgentPersonaPanel, {
      props: { persona: 'You are terse.', canEdit: false },
      global: { stubs },
    })
    expect(wrapper.find('.t-persona__actions').exists()).toBe(false)
    expect(wrapper.find('.persona-input').attributes('readonly')).toBeDefined()
  })
})
