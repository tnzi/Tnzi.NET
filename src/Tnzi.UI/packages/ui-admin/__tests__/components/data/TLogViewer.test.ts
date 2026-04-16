import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TLogViewer from '../../../src/components/data/TLogViewer.vue'
import type { LogEntry } from '../../../src/components/data/TLogViewer.vue'

const sampleEntries: LogEntry[] = [
  { level: 'info', message: 'Service started', timestamp: '2026-04-13T10:00:00Z' },
  { level: 'warn', message: 'Slow query detected', timestamp: '2026-04-13T10:00:01Z' },
  { level: 'error', message: 'Boom failure', timestamp: '2026-04-13T10:00:02Z' },
  { level: 'debug', message: 'Trace step', timestamp: '2026-04-13T10:00:03Z' },
]

describe('TLogViewer', () => {
  it('renders all entries from prop', () => {
    const wrapper = mount(TLogViewer, { props: { entries: sampleEntries } })
    expect(wrapper.findAll('.t-log-viewer__entry')).toHaveLength(4)
  })

  it('filters by level via setLevel imperative API', async () => {
    const wrapper = mount(TLogViewer, { props: { entries: sampleEntries } })
    ;(wrapper.vm as unknown as { setLevel: (v: string) => void }).setLevel('error')
    await nextTick()
    const rows = wrapper.findAll('.t-log-viewer__entry')
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('Boom failure')
  })

  it('filters by text search case-insensitively via setFilter', async () => {
    const wrapper = mount(TLogViewer, { props: { entries: sampleEntries } })
    ;(wrapper.vm as unknown as { setFilter: (v: string) => void }).setFilter('SLOW')
    await nextTick()
    const rows = wrapper.findAll('.t-log-viewer__entry')
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('Slow query detected')
  })

  it('applies level class to each entry', () => {
    const wrapper = mount(TLogViewer, { props: { entries: sampleEntries } })
    expect(wrapper.find('.t-log-viewer__entry--error').exists()).toBe(true)
    expect(wrapper.find('.t-log-viewer__entry--warn').exists()).toBe(true)
    expect(wrapper.find('.t-log-viewer__entry--info').exists()).toBe(true)
    expect(wrapper.find('.t-log-viewer__entry--debug').exists()).toBe(true)
  })

  it('append() adds entry to internal buffer', async () => {
    const wrapper = mount(TLogViewer, { props: { entries: sampleEntries } })
    ;(wrapper.vm as unknown as { append: (e: LogEntry) => void }).append({
      level: 'info',
      message: 'Appended entry',
      timestamp: '2026-04-13T10:00:04Z',
    })
    await nextTick()
    expect(wrapper.findAll('.t-log-viewer__entry')).toHaveLength(5)
  })

  it('clear() empties buffer without mutating prop', async () => {
    const entries = [...sampleEntries]
    const wrapper = mount(TLogViewer, { props: { entries } })
    ;(wrapper.vm as unknown as { clear: () => void }).clear()
    await nextTick()
    expect(wrapper.findAll('.t-log-viewer__entry')).toHaveLength(0)
    expect(entries).toHaveLength(4)
  })
})
