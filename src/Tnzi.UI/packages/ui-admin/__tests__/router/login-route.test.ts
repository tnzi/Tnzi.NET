import { describe, it, expect } from 'vitest'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defaultAdminRoutes } from '../../src/router/routes'
import { LOGIN_MODULES } from '../../src/router/login-modules'

/**
 * The login page switches modules with `router.replace({ name: 'login',
 * params: { module } })`. A named resolve does not check the path's param
 * pattern, so a module the pattern leaves out still lands in the address bar,
 * and a reload of that address then matches no route: the page the user was
 * on (a post-sign-in obligation, say) becomes the 404 page.
 */
describe('login route', () => {
  const router = createRouter({ history: createMemoryHistory(), routes: defaultAdminRoutes })

  it.each(LOGIN_MODULES)('a reload of /login/%s lands back on the login page', (module) => {
    const resolved = router.resolve(`/login/${module}`)
    expect(resolved.name).toBe('login')
    expect(resolved.params.module).toBe(module)
  })

  it('the post-sign-in obligations module is one of them', () => {
    expect(router.resolve('/login/pending-actions').name).toBe('login')
  })

  it('an unknown module still matches nothing', () => {
    expect(router.resolve('/login/not-a-module').name).toBeUndefined()
  })
})
