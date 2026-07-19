/**
 * @tnzi/ui/components/register
 *
 * Global component registration for Tnzi UI (Naive UI).
 *
 * The registration list is derived exhaustively from the components barrel
 * (`./index`, the single source of truth), so every exported `T*` component is
 * registered automatically and the list can never drift from what the package
 * actually exports. This honours the documented contract that
 * `registerComponents: true` registers "all T* components".
 *
 * Tree-shaking note: this module does `import * as components from './index'`
 * (the full barrel), and `plugin.ts` imports it statically. So whenever the
 * `createTnziUi` plugin is used, the whole component barrel is pulled into the
 * bundle EVEN IF `registerComponents: false` — that option only skips the runtime
 * `app.component()` calls, it does not tree-shake. For a minimal bundle, do NOT
 * rely on the plugin's global registration: use the on-demand `TnziUiResolver`
 * (`unplugin-vue-components`, covers every `T*` name) plus direct named imports,
 * so only the components you actually reference are bundled.
 */

import type { App, Component } from 'vue';

import * as components from './index';

/** A runtime export is registrable when it is a Vue component (object or functional). */
function isComponent(value: unknown): value is Component {
  return value != null && (typeof value === 'object' || typeof value === 'function');
}

/**
 * Register all Tnzi UI `T*` components globally.
 * Only called when `registerComponents: true` in plugin options.
 */
export function registerAllComponents(app: App): void {
  for (const [name, value] of Object.entries(components)) {
    // Only T-prefixed components: type-only exports do not exist at runtime, and
    // the sole non-component runtime export (WIDGET_CONTEXT_KEY, a Symbol) is
    // excluded by both the name check and the component guard. Allow a digit after
    // `T` so status/error pages (T403/T404/T500) are also registered.
    if (/^T[A-Z0-9]/.test(name) && isComponent(value)) {
      app.component(name, value);
    }
  }
}
