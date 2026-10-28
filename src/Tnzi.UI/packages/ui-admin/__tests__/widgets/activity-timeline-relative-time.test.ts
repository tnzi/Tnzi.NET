/**
 * `TWidgetActivityTimeline` writes its relative times in the admin's language.
 *
 * The times were English literals ("now", "5m ago"), so a zh-cn dashboard read
 * Chinese around English. They go through `Intl.RelativeTimeFormat` with the
 * active locale's tag, and follow a language switch without a refetch.
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

import { useAdminAppStore } from '../../src/stores/useAdminAppStore'
import TWidgetActivityTimeline from '../../src/components/widgets/TWidgetActivityTimeline.vue'

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()

async function mountWidget() {
  const wrapper = mount(TWidgetActivityTimeline, {
    global: { stubs: { TProjectTimeline: { props: ['items'], template: '<ol><li v-for="i in items" :key="i.key" class="time">{{ i.time }}</li></ol>' } } },
  })
  await flushPromises()
  return wrapper
}

describe('TWidgetActivityTimeline relative times', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetch.mockReset()
    fetch.mockResolvedValue({
      items: [
        { id: 'a', userName: 'alice', functionName: 'Login', startTime: minutesAgo(0) },
        { id: 'b', userName: 'bob', functionName: 'Update', startTime: minutesAgo(5) },
        { id: 'c', userName: 'carol', functionName: 'Delete', startTime: minutesAgo(180) },
      ],
      totalCount: 3,
    })
  })

  it('reads as before in English', async () => {
    const times = (await mountWidget()).findAll('.time').map((n) => n.text())
    expect(times).toEqual(['now', '5m ago', '3h ago'])
  })

  it('follows the admin locale, including a switch after the feed loaded', async () => {
    const wrapper = await mountWidget()

    useAdminAppStore().locale = 'zh-cn'
    await flushPromises()

    expect(wrapper.findAll('.time').map((n) => n.text())).toEqual(['现在', '5分钟前', '3小时前'])
  })
})
