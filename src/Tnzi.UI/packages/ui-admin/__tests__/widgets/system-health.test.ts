/**
 * `TWidgetSystemHealth` must never report a healthy system it could not read.
 *
 * `HttpClient` resolves a refused request (403, diagnostics module missing) with a
 * failed envelope instead of rejecting. The widget used to sniff "raw DTO or
 * envelope" and, on a failed envelope whose `data` is null, read the envelope
 * itself as the summary: `totalCount` undefined -> 0 errors -> "OK".
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { onMounted } from 'vue'
import { NTag } from 'naive-ui'

const get = vi.fn()

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get }) }))
vi.mock('../../src/headless/useWidgetData', () => ({
  useWidgetData: (loader: () => Promise<void>) => {
    onMounted(() => { void loader() })
    return { reload: loader }
  },
}))

import TWidgetSystemHealth from '../../src/components/widgets/TWidgetSystemHealth.vue'

function envelope<T>(data: T, succeeded = true, code = 200) {
  return { code, succeeded, message: succeeded ? '' : 'Forbidden', data }
}

async function mountWidget() {
  const wrapper = mount(TWidgetSystemHealth, { global: { stubs: { TSvgIcon: true } } })
  await flushPromises()
  return wrapper
}

describe('TWidgetSystemHealth', () => {
  beforeEach(() => get.mockReset())

  it('reads the summary through the core diagnostics api', async () => {
    get.mockResolvedValue(envelope({ totalCount: 0, uniqueExceptionCount: 0, windowMinutes: 60 }))

    const wrapper = await mountWidget()

    expect(get).toHaveBeenCalledWith('/admin/diagnostics/exceptions/summary?minutes=60')
    expect(wrapper.findComponent(NTag).props('type')).toBe('success')
  })

  it('classifies a busy hour as down', async () => {
    get.mockResolvedValue(envelope({ totalCount: 25, uniqueExceptionCount: 3, windowMinutes: 60 }))

    const wrapper = await mountWidget()

    expect(wrapper.text()).toContain('25')
    expect(wrapper.findComponent(NTag).props('type')).toBe('error')
  })

  it('does not report healthy when the read is refused', async () => {
    get.mockResolvedValue(envelope(null, false, 403))

    const wrapper = await mountWidget()

    expect(wrapper.findComponent(NTag).props('type')).not.toBe('success')
    expect(wrapper.findComponent(NTag).props('type')).toBe('warning')
  })
})
