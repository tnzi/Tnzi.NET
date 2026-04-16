import { describe, it, expect } from 'vitest'
import { TnziUiAdminResolver } from '../../src/plugin/resolver'

describe('TnziUiAdminResolver', () => {
  const resolver = TnziUiAdminResolver()

  it('resolves TAdminShell to layout path', () => {
    const res = resolver.resolve('TAdminShell')
    expect(res).toBeTruthy()
    expect((res as { from: string }).from).toContain('@tnzi/ui-admin')
    expect((res as { from: string }).from).toContain('TAdminShell')
  })

  it('resolves TCrudPage to crud path', () => {
    const res = resolver.resolve('TCrudPage')
    expect(res).toBeTruthy()
    expect((res as { from: string }).from).toContain('TCrudPage')
  })

  it('resolves TPermissionTree to forms path', () => {
    const res = resolver.resolve('TPermissionTree')
    expect(res).toBeTruthy()
    expect((res as { from: string }).from).toContain('TPermissionTree')
  })

  it('returns undefined for unknown names', () => {
    expect(resolver.resolve('NotATnziAdminComponent')).toBeUndefined()
  })

  it('returns undefined for names without T prefix', () => {
    expect(resolver.resolve('AdminShell')).toBeUndefined()
  })
})
