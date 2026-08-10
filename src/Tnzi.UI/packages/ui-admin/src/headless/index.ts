export * from './useColumnSettings'
export * from './row-actions'
export * from './useBatchActions'
export * from './useFormModal'
export * from './useCrudPage'
export * from './permission-gates'
export * from './useChildCollection'
export * from './defineEnumMeta'
export * from './createAuditHumanizer'
export * from './useEmptyCreateCta'
export * from './useAdminMenu'
export * from './usePermissionGuard'
export * from './useModuleAvailability'
export * from './useRealtimeHub'
export * from './useTabContext'
export * from './useTabTitle'
export * from './use-breadcrumb'
export * from './useAdminModuleManifest'
// Moved to `@tnzi/ui` on 2026-08-02 (see the login stack note below).
export { useFormRules, type Translate, type FormRules } from '@tnzi/ui'
export * from './useNaiveForm'
export * from './useEcharts'
export * from './useRouteProgress'
export * from './useBreakpoint'
export * from './useFileUrl'
export * from './useChatSound'
export * from './chat-sounds'
export * from './useChatRealtime'
// 0.2.72+ (B5): exposed for advanced consumers who want the layout-mode
// derivations without mounting TAdminShell (e.g. building a custom
// shell, headless E2E checks).
export * from './useAdminShellLayout'
export { useDetail } from './useDetail'
export type { DetailMode, DetailAction, DetailLayout, DetailSection, UseDetailOptions, UseDetailReturn } from './useDetail'
export { useGlobalTheme } from './useGlobalTheme'
export type { GlobalThemeController, UseGlobalThemeOptions } from './useGlobalTheme'
export { waitForClientToken } from './waitForClientToken'
// Finance headless layer: shared reporting period, owner/accountant view
// mode, and general-ledger drill-down.
export * from './useFinancePeriod'
export * from './useFinanceViewMode'
export * from './useGlDrilldown'

// --- URL-state primitives --------------------------------------------------
// `useDetail` + `TDetailHost` remain the only sanctioned way to build a detail
// surface; these are the layer underneath it, surfaced because the
// content-page standard documents them as available (a doc promising an import
// that does not resolve is worse than no doc).
export * from './query-scope'
export * from './useSectionRoute'

// Re-measures naive-ui's active-tab underline when a tab label changes size
// after mount (a lazily-loaded count / status badge). `TTabsPage` rides it;
// exposed because any page that hand-rolls `NTabs` has the same gap.
export * from './useTabBarSync'

// Overlay theming - the hook behind `TOverlayTheme`. Needed by anyone who has
// to hand-roll an NModal/NDrawer instead of using TModalShell/TDrawerShell,
// which is exactly when the content-area dark-card theme leaks into it.
export * from './useOverlayTheme'

// --- Login-page building blocks --------------------------------------------
// An app replacing the login route component (`defineAdminApp({ loginComponent })`)
// has to re-derive all of this otherwise: which auth modules the backend
// actually enabled, how to label the account field for the enabled channels,
// the send-code countdown, the image-captcha challenge, and the OAuth buttons.
// The shell ⇄ module contract for the login page. Lives here (not under
// pages/login/) because the three hooks below build on it, and a headless layer
// must not depend on the page layer.
//
// MOVED to `@tnzi/ui` on 2026-08-02. None of it was admin-specific - it is the
// identity domain's login logic, and `@tnzi/ui-ai` needs the same feature
// gating / captcha / two-factor handling for its own sign-in page. Re-exported
// here (by name, not `export *`, which would pull the whole UI package through
// this barrel) so the ~60 existing import sites keep resolving.
export {
  provideLoginContext,
  useLoginContext,
  LOGIN_CONTEXT_KEY,
  DEFAULT_LOGIN_FEATURES,
  type LoginModule,
  type TwoFactorMethodName,
  type TwoFactorChallenge,
  type ResendTwoFactorResult,
  type LoginCaptchaData,
  type PwdLoginPayload,
  type CodeLoginPayload,
  type RegisterPayload,
  type ResetPwdPayload,
  type SendCodePayload,
  type VerifyTwoFactorPayload,
  type LoginCallbackHelpers,
  type LoginCallbacks,
  type LoginUiStyle,
  type LoginSceneState,
  type LoginFeatures,
  type PartialLoginFeatures,
  type LoginThirdPartyProvider,
  type LoginDemoAccount,
  type LoginContext,
  mapAuthConfig,
  mergeFeatures,
  isModuleAvailable,
  firstReachableModule,
  useLoginAccountField,
  type CodeChannels,
  type LoginAccountField,
  useCaptcha,
  type UseCaptchaOptions,
  type UseCaptchaReturn,
  useLoginCaptcha,
  type UseLoginCaptchaReturn,
  buildOAuthProviders,
} from '@tnzi/ui'
export * from './account-type'

// --- Shell / chrome --------------------------------------------------------
// Menu context (which first-level module a route belongs to) pairs with
// `useAdminShellLayout` for consumers assembling a custom shell.
export * from './useAdminMenuContext'
// Label hygiene shared by the permission matrix and the menu admin surfaces.
export * from './code-label'
// Tab-title flash for background activity (the web-IM "you have a message" cue).
export * from './useTitleFlash'
// Declarative widget grid behind `TWorkbenchLayout` (re-exported from @tnzi/ui).
export * from './useWorkbenchLayout'
// Data hook every business widget rides: fires the loader on mount, surfaces
// busy/error through the surrounding card's context, re-runs on refresh.
export * from './useWidgetData'

// --- Realtime --------------------------------------------------------------
// Settings hot-reload (`/hubs/settings`) and the presence auto-away reporter.
export * from './useSettingsRealtime'
export * from './usePresenceActivity'
