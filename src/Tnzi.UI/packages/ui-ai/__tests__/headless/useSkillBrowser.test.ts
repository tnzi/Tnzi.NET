import { describe, it, expect, vi } from 'vitest'
import { useSkillBrowser, type BrowsableSkill } from '../../src/headless/useSkillBrowser'

function mockApi(overrides: any = {}) {
  const skills: BrowsableSkill[] = [
    { id: 's1', slug: 'alpha', name: 'Alpha', description: 'A', category: 'c1', isActive: false, isBuiltIn: false },
    { id: 's2', slug: 'beta', name: 'Beta', description: 'B', category: 'c1', isActive: false, isBuiltIn: true },
  ]
  return {
    getAvailable: vi.fn(async () => ({ data: [...skills] })),
    search: vi.fn(async () => ({ data: [skills[0]!] })),
    activate: vi.fn(async () => undefined),
    delete: vi.fn(async () => undefined),
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

    it('falls back to empty array when data missing', async () => {
      const api = mockApi({ getAvailable: vi.fn(async () => ({})) })
      const b = useSkillBrowser(api)
      await b.loadSkills()
      expect(b.skills.value).toEqual([])
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
    it('activate marks matching skill active', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.activate('alpha', { foo: 'bar' })
      expect(api.activate).toHaveBeenCalledWith('alpha', { foo: 'bar' })
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(true)
      expect(b.skills.value.find((s) => s.slug === 'beta')?.isActive).toBe(false)
    })

    it('deactivate calls delete and marks skill inactive', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      // simulate a previously active skill
      await b.activate('alpha')
      await b.deactivate('alpha')
      expect(api.delete).toHaveBeenCalledWith('s1')
      expect(b.skills.value.find((s) => s.slug === 'alpha')?.isActive).toBe(false)
    })

    it('deactivate no-ops when slug not found', async () => {
      const api = mockApi()
      const b = useSkillBrowser(api)
      await b.loadSkills()
      await b.deactivate('nonexistent')
      expect(api.delete).not.toHaveBeenCalled()
    })
  })
})
