/**
 * The dashboard's audit widgets colour a row by its result.
 *
 * The wire value is the PascalCase enum member name (`'Success'` / `'Failed'` /
 * `'Warning'`, the backend's global `JsonStringEnumConverter`). Both widgets
 * compared against numbers and lowercase strings, so every failed operation
 * rendered with the neutral tone.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { onMounted } from 'vue'

const fetch = vi.fn()

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))
vi.mock('../../src/services/bridges/audit-bridge', () => ({ createAuditBridge: () => ({ logs: { fetch } }) }))
vi.mock('../../src/headless/useWidgetData', () => ({
  useWidgetData: (loader: () => Promise<void>) => {
    onMounted(() => { void loader() })
    return { reload: loader }
  },
}))

import TWidgetAuditRecent from '../../src/components/widgets/TWidgetAuditRecent.vue'
import TWidgetActivityTimeline from '../../src/components/widgets/TWidgetActivityTimeline.vue'
import { auditResultTone } from '../../src/utils/audit-result-tone'

const rows = [
  { id: 'a', userName: 'alice', functionName: 'Login', resultType: 'Success', startTime: new Date().toISOString() },
  { id: 'b', userName: 'bob', functionName: 'Delete', resultType: 'Failed', startTime: new Date().toISOString() },
  { id: 'c', userName: 'carol', functionName: 'Import', resultType: 'Warning', startTime: new Date().toISOString() },
  { id: 'd', userName: 'dave', functionName: 'Read', startTime: new Date().toISOString() },
]

describe('audit result tone on the dashboard widgets', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetch.mockReset()
    fetch.mockResolvedValue({ items: rows, totalCount: rows.length })
  })

  it('maps the serialized enum member names, and nothing else, to a tone', () => {
    expect(['Success', 'Failed', 'Warning', undefined, 1, 'failed'].map(auditResultTone)).toEqual([
      'success',
      'error',
      'warning',
      'default',
      'default',
      'default',
    ])
  })

  it('Recent audit colours a failed operation as an error', async () => {
    const wrapper = mount(TWidgetAuditRecent, {
      global: {
        stubs: {
          Tag: { props: ['type'], template: '<span class="tag" :data-type="type"><slot /></span>' },
          TRelativeTime: true,
        },
      },
    })
    await flushPromises()
    expect(wrapper.findAll('.tag').map((n) => n.attributes('data-type'))).toEqual([
      'success',
      'error',
      'warning',
      'default',
    ])
  })

  it('the activity timeline colours a failed operation as an error', async () => {
    const wrapper = mount(TWidgetActivityTimeline, {
      global: {
        stubs: {
          TProjectTimeline: {
            props: ['items'],
            template: '<ol><li v-for="i in items" :key="i.key" class="item" :data-tone="i.tone" /></ol>',
          },
        },
      },
    })
    await flushPromises()
    expect(wrapper.findAll('.item').map((n) => n.attributes('data-tone'))).toEqual([
      'success',
      'error',
      'warning',
      'default',
    ])
  })
})
