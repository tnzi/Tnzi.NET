/**
 * Global test setup.
 *
 * The bundled locale dictionaries are async chunks (see `src/i18n/messages.ts`)
 * and `defineAdminApp().install()` is what kicks off the fetch. Tests mount
 * components and pages directly, without install(), so nothing would load them
 * and every label would render as its humanised key.
 *
 * Registering `en` synchronously here reproduces the state a real app is in by
 * the time anything paints, so assertions can be about the component rather
 * than about load timing. The dictionary is still lazy in the shipped build -
 * this import exists only in the test graph.
 *
 * The genuinely-not-loaded path is covered explicitly in
 * `__tests__/i18n/messages.test.ts`.
 */
import { en } from '../src/locales/en'
import { zhCn } from '../src/locales/zh-cn'
import { setLocaleMessages } from '../src/i18n/messages'

// Both, because tests assert against both locales (e.g. the exception page's
// Chinese subtitle). Bundle size is not a concern in the test graph, which is
// exactly why the production path does NOT do this.
setLocaleMessages('en', en as unknown as Record<string, unknown>)
setLocaleMessages('zh-cn', zhCn as unknown as Record<string, unknown>)

// Icons. `TSvgIcon` renders `@iconify/vue`'s `Icon`, which asks the public
// Iconify API for any icon that has not been bundled - through happy-dom's
// `fetch`, from every test file that paints an icon. Those requests were still
// in flight when vitest tore the environment down; happy-dom aborts them and
// vitest reports the abort as an unhandled error (a red CI run on 2026-09-04
// with every test passing). Install an API module that never issues a query:
// an unbundled icon renders as an empty span, which is exactly what a consumer
// without network gets, and no test asserts on remote icon data.
// `icon-manifest.test.ts` registers real collections with `addCollection` and
// is unaffected - bundled icons resolve locally without the API.
import { _api } from '@iconify/vue'

_api.setAPIModule('', {
  prepare: () => [],
  send: () => {},
})
