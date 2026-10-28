import type { LoginModule } from '@tnzi/ui'

/**
 * Every module the login page can show, in one list for the two places that
 * must agree on it: the page (which module a `:module` param selects) and the
 * login route's param pattern. The page switches modules by route name, which
 * vue-router resolves without checking the pattern, so a module missing from
 * the pattern still reaches the address bar and a reload of it matches no
 * route.
 */
export const LOGIN_MODULES: readonly LoginModule[] = [
  'pwd-login',
  'code-login',
  'register',
  'reset-pwd',
  'bind-wechat',
  'two-factor',
  'pending-actions',
]
