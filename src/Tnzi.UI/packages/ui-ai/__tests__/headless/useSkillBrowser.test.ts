import { describe, it, expect, vi } from 'vitest'
import { useSkillBrowser } from '../../src/headless/useSkillBrowser'

/** The rows `GET /skills` answers with (`SkillSummaryDto`): no activation state of their own. */
function row(over: Record<string, unknown> = {}) {
  return {
    id: 's1',
    slug: 'alpha',
    scope: 'User',
    name: 'Alpha',
    description: 'A',
    tags: [],
    priority: 0,
    enabled: true,
    source: 'Database',
    isReadOnly: false,
    categoryName: 'c1',
    creationTime: '2026-01-01T00:00:00Z',
    ...over,
  }
}

function ok<T>(data: T) {
  return { succeeded: true, success: true, code: 200, data }
}

function failed(message: string, code = 400) {
  return { succeeded: false, success: false, code, data: undefined, message }
}

function mockApi(overrides: any = {}) {
  const rows = [
    row(),
    row({ id: 's2', slug: 'beta', name: 'Beta', description: 'B', scope: 'System', isReadOnly: true }),
  ]
  return {
    getAvailable: vi.fn(async () => ok([...rows])),
    search: vi.fn(async () => ok([rows[0]!])),
    activate: vi.fn(async (slug: string) => ok({ slug, name: slug, renderedContent: `prompt for ${slug}`, warnings: [] })),
    delete: vi.fn(async () => ok(undefined)),
    ...overrides,
  }
}

describe('useSkillBrowser', () => {
  describe('loadSkills', () => {
    it('populates skills on success', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.skills.value).toHaveLength(2)
      expect(b.isLoading.value).toBe(false)
      expect(b.error.value).toBeNull()
    })

    it('maps the summary rows: nothing is active, and only a user-owned database row is not built in', async () => {
      const api = mockApi({
        getAvailable: vi.fn(async () =>
          ok([
            row(),
            row({ id: 's2', slug: 'beta', scope: 'System', isReadOnly: false }),
            row({ id: 's3', slug: 'gamma', scope: 'User', isReadOnly: true, categoryName: null }),
          ]),
        ),
      })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.skills.value.map((s) => [s.slug, s.isActive, s.isBuiltIn, s.category])).toEqual([
        ['alpha', false, false, 'c1'],
        ['beta', false, true, 'c1'],
        ['gamma', false, true, undefined],
      ])
    })

    it('falls back to empty array when data missing', async () => {
      const api = mockApi({ getAvailable: vi.fn(async () => ({ succeeded: true, success: true, code: 200 })) })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.skills.value).toEqual([])
      expect(b.error.value).toBeNull()
    })

    it('reports a refused envelope instead of showing an empty list', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      api.getAvailable.mockResolvedValueOnce(failed('Forbidden', 403))
      await b.loadSkills()
      expect(b.error.value).toBe('Forbidden')
      expect(b.skills.value).toHaveLength(2)
    })

    it('sets error on rejection', async () => {
      const api = mockApi({ getAvailable: vi.fn(async () => { throw new Error('net') }) })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.error.value).toBe('net')
      expect(b.isLoading.value).toBe(false)
    })

    it('generic error message for non-Error', async () => {
      const api = mockApi({ getAvailable: vi.fn(async () => { throw 'boom' }) })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.error.value).toBe('Failed to load skills')
    })
  })

  describe('loadCategories', () => {
    it('populates categories when categoryApi provided', async () => {
      const getTree = vi.fn(async () => ({ data: [{ id: 'c1', name: 'Cat' }] }))
      const b = useSkillBrowser(mockApi(), { getTree })
      await b.loadCategories()
      expect(b.categories.value).toHaveLength(1)
    })

    it('no-ops silently when categoryApi omitted', async () => {
      const b = useSkillBrowser(mockApi())
      await b.loadCategories()
      expect(b.categories.value).toEqual([])
    })

    it('swallows errors silently', async () => {
      const getTree = vi.fn(async () => { throw new Error('x') })
      const b = useSkillBrowser(mockApi(), { getTree })
      await b.loadCategories()
      expect(b.categories.value).toEqual([])
    })

    it('tolerates missing data field', async () => {
      const getTree = vi.fn(async () => ({}))
      const b = useSkillBrowser(mockApi(), { getTree })
      await b.loadCategories()
      expect(b.categories.value).toEqual([])
    })
  })

  describe('search', () => {
    it('replaces skills with search results', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.search('alpha')
      expect(b.skills.value).toHaveLength(1)
      expect(api.search).toHaveBeenCalledWith('alpha')
    })

    it('keeps a skill activated earlier marked active in the results', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.activate('alpha')
      await b.search('alpha')
      expect(b.skills.value[0]?.isActive).toBe(true)
    })

    it('reports a refused envelope and clears a stale error on the next search', async () => {
      const api = mockApi({ search: vi.fn(async () => failed('Search is unavailable', 503)) })
      const b = useSkillBrowser(api)
      await b.search('q')
      expect(b.error.value).toBe('Search is unavailable')
      api.search.mockResolvedValueOnce(ok([row()]))
      await b.search('q')
      expect(b.error.value).toBeNull()
    })

    it('sets error on search failure', async () => {
      const api = mockApi({ search: vi.fn(async () => { throw new Error('timeout') }) })
      const b = useSkillBrowser(api)
      await b.search('q')
      expect(b.error.value).toBe('timeout')
    })

    it('generic message for non-Error search failure', async () => {
      const api = mockApi({ search: vi.fn(async () => { throw 'x' }) })
      const b = useSkillBrowser(api)
      await b.search('q')
      expect(b.error.value).toBe('Search failed')
    })
  })

  describe('activate/deactivate', () => {
    it('sends the parameters in the body the endpoint binds and returns the rendered prompt', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      const result = await b.activate('alpha', { foo: 'bar' })
      expect(api.activate).toHaveBeenCalledWith('alpha', { parameters: { foo: 'bar' } })
      expect(result?.renderedContent).toBe('prompt for alpha')
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(true)
      expect(b.skills.value.find((s) => s.slug === 'beta')?.isActive).toBe(false)
    })

    it('a refused activation leaves the skill inactive and reports why', async () => {
      const api = mockApi({ activate: vi.fn(async () => failed('Missing required parameter: topic')) })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      const result = await b.activate('alpha')
      expect(result).toBeNull()
      expect(b.error.value).toBe('Missing required parameter: topic')
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(false)
    })

    it('deactivate never deletes the skill: activation has no server-side state to undo', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.activate('alpha')
      await b.deactivate('alpha')
      expect(api.delete).not.toHaveBeenCalled()
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(false)
    })

    it('deactivate is a local change, so it needs nothing beyond the read and activate calls', async () => {
      const { delete: _unused, ...api } = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.activate('alpha')
      await b.deactivate('alpha')
      await b.loadSkills()
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(false)
    })

    it('deactivate no-ops when slug not found', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.deactivate('nonexistent')
      expect(b.skills.value.every((s) => !s.isActive)).toBe(true)
    })
  })
})
