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
