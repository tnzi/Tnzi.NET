import type { App } from 'vue'
import { vPermission } from './vPermission'
import { vModule } from './vModule'

export { vPermission }
export { vModule }

/**
 * Install all `@tnzi/ui-admin` directives on a Vue app instance.
 *
 * Called automatically by `createTnziUiAdmin()` and `defineAdminApp()`.
 * Consumers using neither factory can call this directly.
 */
export function installDirectives(app: App): void {
  app.directive('permission', vPermission)
  app.directive('module', vModule)
}
