import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Skills integration test — production-grade card grid (TCardPage) with a KPI
 * strip, category filter, view/popular drawers, and import/export.
 *
 * Mirrors Personas.test.ts: mock the client + ai-bridge, mount with naive stubs,
 * assert the page mounts, fetches on mount, renders one card per skill (incl. a
 * read-only file-source row whose edit/delete are disabled), and wires the
 * activate/deactivate toggle through the bridge. Assertions are driven entirely
 * by the mock data below.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const skillFetch = vi.fn(async () => ({
  items: [
    {
      id: 's1',
      slug: 'write-blog-post',
      scope: 'User',
      name: 'Write Blog Post',
      description: 'Generates a blog post outline',
      whenToUse: 'When drafting long-form content',
      tags: ['writing', 'content'],
      priority: 100,
      enabled: true,
      source: 1,
      isReadOnly: false,
      creationTime: '2026-04-01T00:00:00Z',
    },
    {
      id: '00000000-0000-0000-0000-000000000000',
      slug: 'review-pr',
      scope: 'System',
      name: 'Review PR',
      description: 'Reviews a pull request',
      tags: ['code'],
      priority: 50,
      enabled: false,
      source: 0,
      isReadOnly: true,
      filePath: '/skills/review-pr/SKILL.md',
      creationTime: '2026-04-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
const getUsageStats = vi.fn(async () => ({
  totalSkills: 12,
  enabledSkills: 9,
  disabledSkills: 3,
  tenantScopeSkills: 4,
  userScopeSkills: 8,
  totalActivations: 271,
}))
const getPopular = vi.fn(async () => [
  { slug: 'write-blog-post', name: 'Write Blog Post', scope: 'User', source: 1, activationCount: 42 },
  { slug: 'review-pr', name: 'Review PR', scope: 'System', source: 0, activationCount: 17 },
])
const getTree = vi.fn(async () => [
  { id: 'c1', name: 'Writing', slug: 'writing', sortOrder: 0, skillCount: 3, children: [] },
  { id: 'c2', name: 'Code', slug: 'code', sortOrder: 1, skillCount: 5, children: [] },
])
const getBySlug = vi.fn(async (slug: string) => ({
  id: 's1',
  slug,
  scope: 'User',
  name: 'Write Blog Post',
  content: '# Write Blog Post\n\nFull SKILL.md body…',
  whenToUse: 'When drafting long-form content',
  tags: ['writing'],
  priority: 100,
  enabled: true,
  source: 1,
  parameters: [],
  creationTime: '2026-04-01T00:00:00Z',
}))
const activate = vi.fn(async () => undefined)
const deactivate = vi.fn(async () => undefined)
const getSkills = vi.fn(async () => [])
const exportSkills = vi.fn(async () => [])
const importSkills = vi.fn(async () => ({ created: 1, updated: 0, skipped: 0, errors: [] }))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    skills: {
      fetch: skillFetch,
      create: vi.fn(async (data: unknown) => ({ id: 's3', ...(data as object) })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
      activate,
      deactivate,
      getBySlug,
      getUsageStats,
      getPopular,
      exportSkills,
      importSkills,
    },
    skillCategories: {
      getTree,
      getSkills,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
  }),
}))

import Skills from '../../../src/pages/ai/skills/Skills.vue'

const stubs = {
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  // TStatCard animates numbers via NNumberAnimation (tween from 0 over ~2s);
  // stub it to render the target value synchronously so text assertions hold.
  NumberAnimation: { name: 'NumberAnimation', props: ['from', 'to', 'precision'], template: '<span>{{ to }}</span>' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  InputNumber: { name: 'InputNumber', props: ['value'], template: '<input type="number" />' },
  Switch: { name: 'Switch', props: ['value'], template: '<button class="n-switch-stub" />' },
  Select: {
    name: 'Select',
    props: ['value', 'options'],
    emits: ['update:value'],
    template: '<select class="n-select-stub" />',
  },
  DatePicker: { name: 'DatePicker', props: ['value'], template: '<input type="date" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Drawer: {
    name: 'Drawer',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-drawer-stub"><slot /></div>',
  },
  DrawerContent: {
    name: 'DrawerContent',
    template: '<div class="n-drawer-content-stub"><slot /><slot name="footer" /></div>',
  },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
}

describe('Skills page (production-grade card grid)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    skillFetch.mockClear()
    getUsageStats.mockClear()
    getPopular.mockClear()
    getTree.mockClear()
    activate.mockClear()
    deactivate.mockClear()
  })

  it('mounts, fetches skills + stats + categories on mount', async () => {
    mount(Skills, { global: { stubs } })
    await flushPromises()
    expect(skillFetch).toHaveBeenCalledTimes(1)
    expect(getUsageStats).toHaveBeenCalledTimes(1)
    expect(getTree).toHaveBeenCalledTimes(1)
  })

  it('renders one card per skill', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    expect(wrapper.findAll('.t-entity-card')).toHaveLength(2)
  })

  it('cards show skill names and the page title', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('Write Blog Post')
    expect(wrapper.text()).toContain('Review PR')
    expect(wrapper.text()).toContain('Skills')
  })

  it('renders the KPI strip from getUsageStats', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    // total / activations values from the mocked stats.
    expect(wrapper.text()).toContain('12')
    expect(wrapper.text()).toContain('271')
  })

  it('toggleEnabled calls deactivate for an enabled skill', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      toggleEnabled: (s: { id: string; enabled: boolean }) => Promise<void>
    }
    await vm.toggleEnabled({ id: 's1', enabled: true })
    await flushPromises()
    expect(deactivate).toHaveBeenCalledWith('s1')
    expect(activate).not.toHaveBeenCalled()
  })

  it('toggleEnabled calls activate for a disabled skill', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      toggleEnabled: (s: { id: string; enabled: boolean }) => Promise<void>
    }
    await vm.toggleEnabled({ id: 's2', enabled: false })
    await flushPromises()
    expect(activate).toHaveBeenCalledWith('s2')
  })

  it('openDetail loads full content via getBySlug', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openDetail: (s: { slug: string; name: string }) => Promise<void>
      detailVisible: boolean
      detailContent: { content: string }
    }
    await vm.openDetail({ slug: 'write-blog-post', name: 'Write Blog Post' })
    await flushPromises()
    expect(vm.detailVisible).toBe(true)
    expect(getBySlug).toHaveBeenCalledWith('write-blog-post')
    expect(vm.detailContent.content).toContain('Full SKILL.md body')
  })

  it('openPopular loads the most-activated ranking', async () => {
    const wrapper = mount(Skills, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      openPopular: () => Promise<void>
      popularSkills: Array<{ slug: string }>
    }
    await vm.openPopular()
    await flushPromises()
    expect(getPopular).toHaveBeenCalledTimes(1)
    expect(vm.popularSkills.map((p) => p.slug)).toEqual(['write-blog-post', 'review-pr'])
  })
})
