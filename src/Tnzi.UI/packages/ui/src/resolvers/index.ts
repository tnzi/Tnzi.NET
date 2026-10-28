/**
 * @tnzi/ui/resolvers
 *
 * Component resolvers for unplugin-vue-components.
 */

import { TNZI_UI_COMPONENT_NAMES } from './component-names';

/**
 * Component resolver for unplugin-vue-components.
 *
 * Resolves only the components this package exports. unplugin-vue-components
 * asks its resolvers in order and takes the first answer, so any other name
 * (another `@tnzi/*` package's component, or the app's own `T*` component) is
 * left to the resolvers after this one.
 *
 * ```ts
 * // vite.config.ts
 * import Components from 'unplugin-vue-components/vite';
 * import { TnziUiResolver } from '@tnzi/ui/resolvers';
 *
 * export default defineConfig({
 *   plugins: [
 *     Components({
 *       resolvers: [TnziUiResolver()],
 *     }),
 *   ],
 * });
 * ```
 */
export function TnziUiResolver() {
  return {
    type: 'component' as const,
    resolve: (name: string) => (TNZI_UI_COMPONENT_NAMES.has(name) ? { name, from: '@tnzi/ui' } : undefined),
  };
}
