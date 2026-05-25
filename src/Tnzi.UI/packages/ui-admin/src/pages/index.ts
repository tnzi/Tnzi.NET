// Phase 2b Task 2.32: Re-exports the first rewritten page (UserManagement PoC).
// Remaining pages (authorization/system/...) are rewritten incrementally in Phase 3.
export { default as UserManagement } from './identity/UserManagement.vue'

// Translation helpers — host apps build their own `t()` against the same
// resolver the framework's pages use. Picks up consumer-supplied messages
// registered via `useAdminAppStore.extendLocaleMessages`.
export {
  translatePageKey,
  makePageTranslator,
  interpolate,
  maybeTranslate,
} from './_shared/translate'

// Phase I.7.1: login page route component + 5 modules + composable.
// Consumers can import `{ TnziAdminLoginPage }` to register the route
// themselves, or override individual modules via the `moduleComponents`
// prop on `TLoginPage`.
export { default as TnziAdminLoginPage } from './login/index.vue'
export { default as PwdLoginModule } from './login/modules/PwdLogin.vue'
export { default as CodeLoginModule } from './login/modules/CodeLogin.vue'
export { default as RegisterModule } from './login/modules/Register.vue'
export { default as ResetPwdModule } from './login/modules/ResetPwd.vue'
export { default as BindWechatModule } from './login/modules/BindWechat.vue'
export { default as TwoFactorChallengeModule } from './login/modules/TwoFactorChallenge.vue'
export {
  useLoginContext,
  provideLoginContext,
  LOGIN_CONTEXT_KEY,
  type LoginContext,
  type LoginModule,
  type LoginCallbacks,
  type LoginCallbackHelpers,
  type LoginDemoAccount,
  type PwdLoginPayload,
  type CodeLoginPayload,
  type RegisterPayload,
  type ResetPwdPayload,
  type SendCodePayload,
  type VerifyTwoFactorPayload,
  type TwoFactorChallenge,
} from './login/useLoginContext'
