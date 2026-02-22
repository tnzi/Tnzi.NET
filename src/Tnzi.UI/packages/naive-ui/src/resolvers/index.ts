/**
 * @tnzi/naive-ui/resolvers
 *
 * Component resolvers for unplugin-vue-components.
 */

export interface TnziNaiveUiResolverOptions {
  /** Component prefix (default: 'T') */
  prefix?: string;
}

/**
 * Component resolver for unplugin-vue-components.
 *
 * ```ts
 * // vite.config.ts
 * import Components from 'unplugin-vue-components/vite';
 * import { TnziNaiveUiResolver } from '@tnzi/naive-ui/resolvers';
 *
 * export default defineConfig({
 *   plugins: [
 *     Components({
 *       resolvers: [TnziNaiveUiResolver()],
 *     }),
 *   ],
 * });
 * ```
 */
export function TnziNaiveUiResolver(options: TnziNaiveUiResolverOptions = {}) {
  const prefix = options.prefix ?? 'T';

  return {
    type: 'component' as const,
    resolve: (name: string) => {
      if (name.startsWith(prefix)) {
        return {
          name,
          from: '@tnzi/naive-ui',
        };
      }
      return undefined;
    },
  };
}
