/**
 * @tnzi/ui/resolvers
 *
 * Component resolvers for unplugin-vue-components.
 */

export interface TnziUiResolverOptions {
  /** Component prefix (default: 'T') */
  prefix?: string;
}

/**
 * Component resolver for unplugin-vue-components.
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
export function TnziUiResolver(options: TnziUiResolverOptions = {}) {
  const prefix = options.prefix ?? 'T';

  return {
    type: 'component' as const,
    resolve: (name: string) => {
      if (name.startsWith(prefix)) {
        return {
          name,
          from: '@tnzi/ui',
        };
      }
      return undefined;
    },
  };
}
