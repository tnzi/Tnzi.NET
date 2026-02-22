/**
 * @tnzi/shadcn/resolvers/shadcn-resolver
 *
 * Tnzi UI components auto-import resolver.
 */

import type { ComponentResolver } from 'unplugin-vue-components';

export interface TnziUiResolverOptions {
  /** Component prefix */
  prefix?: string;
}

export function TnziUiResolver(options: TnziUiResolverOptions = {}): ComponentResolver {
  const { prefix = 'T' } = options;

  return {
    type: 'component',
    resolve: (name) => {
      if (name.startsWith(prefix)) {
        return {
          name: name,
          from: '@tnzi/shadcn',
        };
      }
      return undefined;
    },
  };
}
