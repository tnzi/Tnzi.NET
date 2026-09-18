import { describe, it, expect, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useCrudPage, type UseCrudPageReturn } from '../../src/headless/useCrudPage'
import { useDetail } from '../../src/headless/useDetail'

/**
 * One route, two overlay engines, one query key.
 *
 * A list page that never opens the crud engine's own overlay (no create / edit
 * form) but shows a read-only drawer through a page-level `useDetail` ends up
 * with two engines reconciling `?detail=` on every route change: the crud
 * engine claims the key by default, the page engine claims it explicitly.
 * A refreshed or shared `?detail=view:<id>` for a record outside the loaded
 * page is resolved by the page engine (it has `loadData`), then the crud
 * engine's items arrive, it cannot find the id (no `loadDetailById`), and it
 * deletes the key - which closes the drawer the page engine just opened.
 * 2026-09-12: Deposits / EftBatches / Receipts shipped this way.
 *
 * The fix on those pages is `detailUrl: false` on the crud engine (its overlay
 * is never opened, so it has nothing to deep-link). This test pins the
 * behaviour on both sides: the shared-key shape loses the drawer (documents the
 * defect so a regression reads as a red test, not as a mystery), and the
 * opted-out shape keeps it.
 */

interface Foo {
  id: string
  name: string
}

const PAGE_ONE: Foo[] = [
  { id: 'a1', name: 'Alpha' },
  { id: 'b2', name: 'Beta' },
]

async function harness(crudDetailUrl: boolean | undefined, initialPath: string) {
  let crud: UseCrudPageReturn<Foo> = null as never
  let drawer: ReturnType<typeof useDetail<Foo>> = null as never
  const loadData = vi.fn(async (id: string | number) => ({ id: String(id), name: 'Loaded ' + String(id) }))
  const Comp = defineComponent({
    setup() {
      crud = useCrudPage<Foo>({
        pageId: 'test.shared-key',
        columns: [],
        rowKey: (r) => r.id,
        fetchData: vi.fn(async () => ({
          items: PAGE_ONE,
          totalCount: 40,
          pageIndex: 1,
          pageSize: 20,
          totalPages: 2,
          hasPreviousPage: false,
          hasNextPage: true,
        })),
        retryFetch: 0,
        ...(crudDetailUrl === undefined ? {} : { detailUrl: crudDetailUrl }),
      })
      drawer = useDetail<Foo>({ mode: 'drawer', url: 'detail', loadData })
      return () => null
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/list', name: 'list', component: { template: '<div/>' } }],
  })
  router.push(initialPath)
  await router.isReady()
  mount(Comp, { global: { plugins: [router] } })
  await flushPromises()
  // The page's list load, as the shell would trigger it on mount.
  await crud.refresh()
  await flushPromises()
  return { crud: () => crud, drawer: () => drawer, router, loadData }
}

describe('useCrudPage + page-level useDetail on one route', () => {
  it('opted out (detailUrl: false): a deep link to a record off the loaded page opens the drawer and keeps the key', async () => {
    const { drawer, router, loadData } = await harness(false, '/list?detail=view:zz9')
    expect(loadData).toHaveBeenCalledWith('zz9')
    expect(drawer().visible.value).toBe(true)
    expect(drawer().data.value?.id).toBe('zz9')
    expect(router.currentRoute.value.query.detail).toBe('view:zz9')
  })

  it('shared key (the defect shape): the crud engine wipes the key once its items arrive and the drawer is lost', async () => {
    const { drawer, router } = await harness(undefined, '/list?detail=view:zz9')
    // The page engine resolved the id through loadData, the crud engine could not
    // (id not in items, no loadDetailById) and replaced the URL without the key,
    // which the page engine reconciles as "close".
    expect(router.currentRoute.value.query.detail).toBeUndefined()
    expect(drawer().visible.value).toBe(false)
  })
})
