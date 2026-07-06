import { describe, it, expect } from 'vitest'
import { defineComponent, ref, type Ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'
import { useSectionRoute, type UseSectionRouteOptions } from '../../src/headless/useSectionRoute'
import { ADMIN_DEEP_LINK_KEY } from '../../src/plugin/deepLinkConfig'

const sections = [{ key: 'a' }, { key: 'b' }, { key: 'c' }]

function makeRouter(initialPath: string): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/d/:id', name: 'd', component: { template: '<div/>' } }],
  })
  router.push(initialPath)
  return router
}

/**
 * Mount a throwaway component that wires `useSectionRoute` inside a real
 * vue-router so the composable's query reads/writes exercise the genuine
 * history stack (the whole point of the deep-link + Back/Forward behaviour).
 */
async function harness(options: UseSectionRouteOptions, initialPath = '/d/1') {
  let section: Ref<string | null> = null as unknown as Ref<string | null>
  const Comp = defineComponent({
    setup() {
      section = useSectionRoute(options)
      return () => null
    },
  })
  const router = makeRouter(initialPath)
  await router.isReady()
  mount(Comp, { global: { plugins: [router] } })
  await flushPromises()
  return { section: () => section, router }
}

describe('useSectionRoute', () => {
  it('initialises from the URL query section (deep link)', async () => {
    const { section } = await harness({ sections, defaultSection: 'a' }, '/d/1?section=b')
    expect(section().value).toBe('b')
  })

  it('writes the resolved default into the query so it is shareable', async () => {
    const { section, router } = await harness({ sections, defaultSection: 'a' }, '/d/1')
    expect(section().value).toBe('a')
    expect(router.currentRoute.value.query.section).toBe('a')
  })

  it('pushes on switch so Back steps through the previous sections', async () => {
    const { section, router } = await harness({ sections, defaultSection: 'a' }, '/d/1')
    // default 'a' is written via replace (no history entry)
    expect(router.currentRoute.value.query.section).toBe('a')

    section().value = 'b'
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBe('b')

    section().value = 'c'
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBe('c')

    // Browser Back → the query returns to 'b' AND the ref follows (UI updates).
    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBe('b')
    expect(section().value).toBe('b')

    router.back()
    await flushPromises()
    expect(section().value).toBe('a')

    // Forward returns to 'b'.
    router.forward()
    await flushPromises()
    expect(section().value).toBe('b')
  })

  it('ignores an invalid section and falls back to the default', async () => {
    const { section } = await harness({ sections, defaultSection: 'a' }, '/d/1?section=zzz')
    expect(section().value).toBe('a')
  })

  it('uses a custom query key', async () => {
    const { section, router } = await harness(
      { sections, defaultSection: 'a', key: 'tab' },
      '/d/1?tab=c',
    )
    expect(section().value).toBe('c')
    section().value = 'b'
    await flushPromises()
    expect(router.currentRoute.value.query.tab).toBe('b')
  })

  it('keeps sibling business query params untouched when syncing', async () => {
    const { section, router } = await harness({ sections, defaultSection: 'a' }, '/d/1?page=2&kw=x')
    await flushPromises()
    section().value = 'b'
    await flushPromises()
    // Business params ride along verbatim — the nav only owns its own key.
    expect(router.currentRoute.value.query).toEqual({ page: '2', kw: 'x', section: 'b' })
  })

  it('honours replace history so sections stay out of the back stack', async () => {
    const { section, router } = await harness(
      { sections, defaultSection: 'a', history: 'replace' },
      '/d/1',
    )
    section().value = 'b'
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBe('b')
    section().value = 'c'
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBe('c')
  })

  it('does not touch the URL when disabled', async () => {
    const { section, router } = await harness(
      { sections, defaultSection: 'a', enabled: false },
      '/d/1',
    )
    expect(section().value).toBe('a')
    expect(router.currentRoute.value.query.section).toBeUndefined()
    section().value = 'b'
    await flushPromises()
    expect(router.currentRoute.value.query.section).toBeUndefined()
  })

  it('degrades to a plain ref without a router', () => {
    const s = useSectionRoute({ sections, defaultSection: 'b' })
    expect(s.value).toBe('b')
    s.value = 'c'
    expect(s.value).toBe('c')
  })

  // A page detail and a modal detail opened on top of it each track their OWN
  // section in ONE URL without clobbering each other — namespacing is native to
  // the query string (one key per owner).
  it('coexists with a sibling key in one URL (nested page + overlay sections)', async () => {
    const overlayOn = ref(false)
    let pageSection: Ref<string | null> = null as never
    let overlaySection: Ref<string | null> = null as never
    const Comp = defineComponent({
      setup() {
        pageSection = useSectionRoute({ sections, defaultSection: 'a', key: 'section' })
        // The overlay activates later (when opened) — mirrors a modal opening on
        // top of the page so the two initial writes never race.
        overlaySection = useSectionRoute({
          sections,
          defaultSection: 'c',
          key: 'edit',
          enabled: () => overlayOn.value,
        })
        return () => null
      },
    })
    const router = makeRouter('/d/1')
    await router.isReady()
    mount(Comp, { global: { plugins: [router] } })
    await flushPromises()

    // Page wrote its key; overlay is closed so its key is absent.
    expect(router.currentRoute.value.query).toEqual({ section: 'a' })

    // Open the overlay → its key is ADDED alongside the page's.
    overlayOn.value = true
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ section: 'a', edit: 'c' })

    // Switch each independently — neither clobbers the other.
    pageSection.value = 'b'
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ section: 'b', edit: 'c' })
    overlaySection.value = 'a'
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ section: 'b', edit: 'a' })

    // Close the overlay → ONLY its key is dropped; the page's survives.
    overlayOn.value = false
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ section: 'b' })
    expect(pageSection.value).toBe('b')
  })

  it('adopts a deep-linked overlay key when re-enabled (shared link reopen)', async () => {
    const overlayOn = ref(false)
    let overlaySection: Ref<string | null> = null as never
    const Comp = defineComponent({
      setup() {
        overlaySection = useSectionRoute({
          sections,
          defaultSection: 'a',
          key: 'edit',
          enabled: () => overlayOn.value,
        })
        return () => null
      },
    })
    // The shared link already carries the overlay's section.
    const router = makeRouter('/d/1?section=b&edit=c')
    await router.isReady()
    mount(Comp, { global: { plugins: [router] } })
    await flushPromises()
    // Closed → plain ref on its default, not yet reading the URL.
    expect(overlaySection.value).toBe('a')

    overlayOn.value = true
    await flushPromises()
    // Opened → adopts the deep-linked 'c' instead of overwriting it with 'a'.
    expect(overlaySection.value).toBe('c')
    expect(router.currentRoute.value.query).toEqual({ section: 'b', edit: 'c' })
  })

  it('app-wide deepLink.section=false degrades to a plain ref (no URL writes, deep link ignored)', async () => {
    let section: Ref<string | null> = null as never
    const Comp = defineComponent({
      setup() {
        section = useSectionRoute({ sections, defaultSection: 'a' })
        return () => null
      },
    })
    const router = makeRouter('/d/1?section=b')
    await router.isReady()
    mount(Comp, {
      global: {
        plugins: [router],
        provide: { [ADMIN_DEEP_LINK_KEY as symbol]: { detail: true, section: false } },
      },
    })
    await flushPromises()

    // Deep-linked ?section=b is NOT adopted; the ref sits on the default.
    expect(section.value).toBe('a')

    // Switching sections still works but never touches the URL.
    section.value = 'c'
    await flushPromises()
    expect(section.value).toBe('c')
    expect(router.currentRoute.value.query.section).toBe('b')
  })
})
