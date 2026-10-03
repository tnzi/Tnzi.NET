// Phase 2b Task 2.32: Re-exports the first rewritten page (Users PoC).
// Remaining pages (authorization/system/...) are rewritten incrementally in Phase 3.
export { default as Users } from './identity/Users.vue'

// Sign-in security of one account (second factor + sign-in IP allow-list),
// hostable inside a consumer's own persona page (staff record, officer file,
// customer account) with nothing but a `userId`. Self-contained on purpose:
// it resolves its own strings and reads `user.security` itself, so a host
// grants `user.view` (+ `user.security` for writes) and never has to expose
// the framework Users page or the Roles / Sessions / Login-logs grants its
// other sections need.
export { default as UserSecuritySection } from './identity/sections/UserSecuritySection.vue'
// The same two blocks without the section chrome, for a host whose record
// page already has the section they belong in (an "Access" section with the
// login window and the password reset): they go in under the host's own
// blocks, inside the host's `TDetailSection`. One rule is drawn between the
// two; the host draws what separates them from its own.
export { default as UserSecurityBlocks } from './identity/sections/UserSecurityBlocks.vue'

// Translation helpers - host apps build their own `t()` against the same
// resolver the framework's pages use. Picks up consumer-supplied messages
// registered via `useAdminAppStore.extendLocaleMessages`.
export {
  translatePageKey,
  makePageTranslator,
  interpolate,
  maybeTranslate,
} from './_shared/translate'

// Admin form-schema renderer - `TSchemaForm` (from `@tnzi/ui`) wrapped with the
// admin field renderers (icon/json/password) + the shared `selectRenderer`
// factory for dynamic / parent-dependent select options. Exported so consumer
// pages compose the same schema-driven form (with dynamic selects) instead of
// rebuilding a wrapper over the bare `@tnzi/ui` `TSchemaForm`.
export { default as TFormSchemaRenderer, selectRenderer } from './_shared/form-schema'
export type {
  FormSchemaItem,
  FormSchemaFieldType,
  FieldRenderer,
  FieldRenderContext,
  SelectRendererOptions,
} from './_shared/form-schema'

// User Center - Profile extension-block contract. A block registered via
// `userCenter.profile.extra` calls `useUserCenterProfileExtra({ save, reset?,
// dirty? })` from its setup to join the built-in Profile section's single
// Reset/Save pair (one form, one pair of buttons) instead of shipping its own
// save button. The two writes are NOT atomic - read the module's contract.
export {
  useUserCenterProfileExtra,
  provideUserCenterProfileExtra,
  createUserCenterProfileExtraRegistry,
  USER_CENTER_PROFILE_EXTRA_KEY,
  type UserCenterProfileExtraHandler,
  type UserCenterProfileExtraRegistry,
} from './account/useUserCenterProfileExtra'

// Phase I.7.1: login page route component + 5 modules + composable.
// Consumers can import `{ TnziAdminLoginPage }` to register the route
// themselves, or override individual modules via the `moduleComponents`
// prop on `TLoginPage`.
export { default as TnziAdminLoginPage } from './login/LoginView.vue'
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
  type LoginThirdPartyProvider,
  type LoginUiStyle,
  type PwdLoginPayload,
  type CodeLoginPayload,
  type RegisterPayload,
  type ResetPwdPayload,
  type SendCodePayload,
  type VerifyTwoFactorPayload,
  type TwoFactorChallenge,
} from '@tnzi/ui'
