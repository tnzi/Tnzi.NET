import { afterEach, describe, it, expect } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import TWorkbenchLayout from '../../../src/components/layout/TWorkbenchLayout.vue'
import type { SpanValue } from '../../../src/components/layout/widget-types'

/**
 * The fixed layout hands a widget's span to naive's NGrid (`responsive="screen"`,
 * breakpoints s 640 / m 1024 / l 1280 / xl 1536); the draggable layout resolves
 * the same span itself. It used Tailwind's 768 / 1024 instead, so one
 * `{ md: 12, lg: 6 }` widget took a different width in each mode and the page
 * reflowed whenever the user switched drag-to-reorder on or off.
 */
const Widget = defineComponent({ setup: () => () => h('div', 'w') })
const originalWidth = window.innerWidth

afterEach(() => {
  Object.defineProperty(window, 'innerWidth', { value: originalWidth, configurable: true })
})

async function draggableSpanAt(width: number, span: SpanValue): Promise<string | undefined> {
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true })
  const wrapper = mount(TWorkbenchLayout, {
    props: { widgets: [{ id: 'w', component: Widget, span }], layout: 'draggable' },
  })
  await flushPromises()
  const style = (wrapper.find('.t-workbench-layout__item').element as HTMLElement).style.gridColumn
  wrapper.unmount()
  return style.split('/')[0]?.trim()
}

describe('TWorkbenchLayout - draggable spans follow the NGrid breakpoints', () => {
  const span = { xs: 24, md: 12, lg: 6 }

  it.each([
    [900, 'span 24'],
    [1100, 'span 12'],
    [1300, 'span 6'],
  ])('at %ipx resolves %s', async (width, expected) => {
    expect(await draggableSpanAt(width, span)).toBe(expected)
  })

  it('falls back to a full row below the smallest breakpoint given, as NGrid does', async () => {
    expect(await draggableSpanAt(500, { lg: 6 })).toBe('span 24')
  })
})
