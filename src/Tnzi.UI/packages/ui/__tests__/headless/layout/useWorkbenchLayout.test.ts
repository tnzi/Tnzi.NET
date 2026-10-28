import { describe, it, expect, beforeEach } from 'vitest'
import { nextTick, ref } from 'vue'
import { useWorkbenchLayout } from '../../../src/headless/layout/useWorkbenchLayout'
import type { WidgetDef } from '../../../src/components/layout/widget-types'

const KEY = 'test-workbench-order'

function widget(id: string, pinned = false): WidgetDef {
  return { id, component: {}, pinned } as unknown as WidgetDef
}

function ids(list: WidgetDef[]): string[] {
  return list.map((w) => w.id)
}

describe('useWorkbenchLayout', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('keeps declaration order when nothing is persisted', () => {
    const widgets = ref([widget('a'), widget('b'), widget('footer', true)])
    const { orderedWidgets } = useWorkbenchLayout({ widgets, draggable: ref(true), persistKey: KEY })
    expect(ids(orderedWidgets.value)).toEqual(['a', 'b', 'footer'])
  })

  // A pinned widget is "pinned to its position": reordering the others must not
  // move it, whether or not an order has been saved yet.
  it('keeps a pinned widget at its declared slot after the others are reordered', () => {
    const widgets = ref([widget('banner', true), widget('a'), widget('b'), widget('c'), widget('footer', true)])
    const { orderedWidgets, setOrder } = useWorkbenchLayout({ widgets, draggable: ref(true), persistKey: KEY })

    // The user drags `c` to the very top, past the pinned banner.
    const byId = new Map(widgets.value.map((w) => [w.id, w]))
    setOrder(['c', 'banner', 'a', 'b', 'footer'].map((id) => byId.get(id)!))

    expect(ids(orderedWidgets.value)).toEqual(['banner', 'c', 'a', 'b', 'footer'])
  })

  it('appends widgets missing from the saved order around the pinned slots', () => {
    window.localStorage.setItem(KEY, JSON.stringify(['b', 'gone', 'a']))
    const widgets = ref([widget('a'), widget('b'), widget('footer', true), widget('new')])
    const { orderedWidgets } = useWorkbenchLayout({ widgets, draggable: ref(true), persistKey: KEY })
    expect(ids(orderedWidgets.value)).toEqual(['b', 'a', 'footer', 'new'])
  })

  it('restores the saved order when draggable is switched off and back on', async () => {
    const widgets = ref([widget('a'), widget('b'), widget('c')])
    const draggable = ref(true)
    const { orderedWidgets, setOrder } = useWorkbenchLayout({ widgets, draggable, persistKey: KEY })
    const [a, b, c] = widgets.value
    setOrder([c!, a!, b!])
    expect(ids(orderedWidgets.value)).toEqual(['c', 'a', 'b'])

    draggable.value = false
    await nextTick()
    expect(ids(orderedWidgets.value)).toEqual(['a', 'b', 'c'])

    draggable.value = true
    await nextTick()
    expect(ids(orderedWidgets.value)).toEqual(['c', 'a', 'b'])
    expect(JSON.parse(window.localStorage.getItem(KEY)!)).toEqual(['c', 'a', 'b'])
  })
})
