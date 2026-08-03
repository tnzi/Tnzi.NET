import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { NPagination } from 'naive-ui'
import TListPager from '../../src/components/data/TListPager.vue'

/**
 * The list footer pager.
 *
 * It is its own component so a list that does NOT ride `TListShell` (an
 * embedded table, a drawer list, a tab pane with its own layout) gets the same
 * footer instead of reproducing it from a bare NPagination - which is how
 * footers drift into `size="small"` on one page and medium on the next, with
 * and without the total.
 */
describe('TListPager', () => {
  const mountPager = (props: Record<string, unknown> = {}) =>
    mount(TListPager, {
      props: { page: 1, pageSize: 20, itemCount: 57, ...props },
      // The stub needs an explicit `name`: `findComponent({ name })` matches
      // the component's own name, and an anonymous object stub has none.
      global: {
        stubs: {
          NPagination: {
            name: 'NPagination',
            props: ['page', 'pageSize', 'itemCount', 'pageSizes', 'showSizePicker', 'simple', 'prefix'],
            template: '<div class="pager-stub" />',
          },
        },
      },
    })

  it('forwards the controlled page state', () => {
    const stub = mountPager().findComponent(NPagination)
    expect(stub.props('page')).toBe(1)
    expect(stub.props('pageSize')).toBe(20)
    expect(stub.props('itemCount')).toBe(57)
  })

  it('offers the standard page sizes with a total prefix', () => {
    const stub = mountPager().findComponent(NPagination)
    expect(stub.props('showSizePicker')).toBe(true)
    expect(stub.props('pageSizes')).toEqual([10, 20, 50, 100])
    // The prefix is what carries "Total N" - a list footer without it forces
    // the reader to page to the end to learn how many rows there are.
    const prefix = stub.props('prefix') as (arg: { itemCount?: number }) => string
    expect(prefix({ itemCount: 57 })).toContain('57')
  })

  it('lets a fixed-size list drop the size picker', () => {
    const stub = mountPager({ showSizePicker: false }).findComponent(NPagination)
    expect(stub.props('showSizePicker')).toBe(false)
  })

  it('re-emits both update events so it stays controlled', async () => {
    const wrapper = mountPager()
    const stub = wrapper.findComponent(NPagination)

    stub.vm.$emit('update:page', 3)
    stub.vm.$emit('update:page-size', 50)
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:page')?.[0]).toEqual([3])
    expect(wrapper.emitted('update:pageSize')?.[0]).toEqual([50])
  })

  it('takes a translate override so a non-admin host can label it', () => {
    const translate = vi.fn(() => 'Всего')
    const stub = mountPager({ translate }).findComponent(NPagination)
    const prefix = stub.props('prefix') as (arg: { itemCount?: number }) => string
    expect(prefix({ itemCount: 5 })).toBe('Всего 5')
  })
})
