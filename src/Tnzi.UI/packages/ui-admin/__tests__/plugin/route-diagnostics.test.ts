import { describe, it, expect, vi, afterEach } from 'vitest'
import type { RouteRecordRaw } from 'vue-router'
import { warnMisplacedTopLevelRoutes } from '../../src/plugin/route-diagnostics'

const Stub = { render: () => null }

function route(path: string): RouteRecordRaw {
  return { path, component: Stub, meta: { requiresAuth: false } } as RouteRecordRaw
}

function captureWarnings(fn: () => void): string[] {
  const spy = vi.spyOn(console, 'warn').mockImplementation(() => {})
  try {
    fn()
    return spy.mock.calls.map((c) => String(c[0]))
  } finally {
    spy.mockRestore()
  }
}

describe('warnMisplacedTopLevelRoutes', () => {
  afterEach(() => vi.restoreAllMocks())

  it('stays silent on prefix-free paths (the correct shape)', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes(
        [route('/sign/:token'), route('/doc/:token'), route('/forms/:token')],
        '/admin',
      ),
    )
    expect(warnings).toEqual([])
  })

  it('stays silent on prefix-free paths under basePath "/" too', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes([route('/forms/:token')], '/'),
    )
    expect(warnings).toEqual([])
  })

  it('names a path that already carries the basePath (the double-prefix trap)', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes([route('/admin/forms/:token')], '/admin'),
    )
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('/admin/forms/:token')
    // Says what it resolves to AND what to write instead.
    expect(warnings[0]).toContain('/admin/admin/forms/:token')
    expect(warnings[0]).toContain('"/forms/:token"')
  })

  it('flags a path equal to the basePath itself', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes([route('/console')], '/console'),
    )
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('already carries basePath')
  })

  it('does not mistake a path that merely shares a prefix segment', () => {
    // '/admin-tools' starts with '/admin' as a STRING but is a different segment.
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes([route('/admin-tools/:token')], '/admin'),
    )
    expect(warnings).toEqual([])
  })

  it('flags a relative top-level path', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes([route('forms/:token')], '/admin'),
    )
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('not absolute')
  })

  it('flags a route claiming the app root', () => {
    const warnings = captureWarnings(() => warnMisplacedTopLevelRoutes([route('/')], '/admin'))
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('app root')
  })

  it('reports every offending record, not just the first', () => {
    const warnings = captureWarnings(() =>
      warnMisplacedTopLevelRoutes(
        [route('/admin/sign/:token'), route('/doc/:token'), route('/admin/forms/:token')],
        '/admin',
      ),
    )
    expect(warnings).toHaveLength(2)
  })
})
