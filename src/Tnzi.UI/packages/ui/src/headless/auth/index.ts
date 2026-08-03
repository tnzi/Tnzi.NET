/**
 * Authentication headless logic - the login stack.
 *
 * Moved down from `@tnzi/ui-admin` on 2026-08-02 because none of it is
 * admin-specific: it is the identity domain's login logic (backend feature
 * gating via `GET /auth/config`, two-factor challenges, image captchas,
 * account-type detection, the OAuth hand-off), and `@tnzi/ui-ai` needs exactly
 * the same for its own sign-in page. It belongs to the layer both packages
 * build on.
 *
 * Every hook takes its copy through an injected
 * `Translate` = `(key, fallback?) => string`, so a consumer wires whichever
 * i18n mechanism it already has; none of them reach for a global catalogue.
 * That is what lets `@tnzi/ui-admin` (namespace translator) and `@tnzi/ui-ai`
 * (object-tree locale) share this code without either changing.
 *
 * An earlier, much thinner generation lived here too - `useLoginForm` /
 * `useRegisterForm` / `usePasswordReset` plus `TLoginForm` and friends, built
 * on a bespoke `LoginProvider` abstraction parallel to core's `authApi`, with
 * hard-coded English validation messages. It was removed on 2026-08-02 once its
 * last consumer moved to the stack above: two generations of the same thing,
 * one of them knowing nothing about captchas or two-factor, is exactly the
 * fork a consumer picks the wrong side of.
 */
export * from './useLoginContext'
export * from './login-features'
export * from './useFormRules'
export * from './useLoginAccountField'
export * from './useCaptcha'
export * from './useLoginCaptcha'
export * from './oauth-providers'
export * from './default-auth'
