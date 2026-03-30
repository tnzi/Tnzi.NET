/**
 * @tnzi/shadcn/resolvers/shadcn-resolver
 *
 * Tnzi UI components auto-import resolver.
 */

import type { ComponentResolver } from 'unplugin-vue-components';

/**
 * Set of all exported T-prefixed component names.
 * Only these components will be auto-resolved.
 */
const validComponents = new Set([
  // Auth
  'TLoginForm',
  'TRegisterForm',
  'TPasswordReset',
  // Table
  'TDataTable',
  // List
  'TDataList',
  // Form
  'TForm',
  'TDynamicForm',
  'TSearchForm',
  // Card
  'TUserCard',
  'TStatCard',
  // Layout
  'TAdminLayout',
  'TSidebar',
  'THeader',
  'TBreadcrumb',
  // Navigation
  'TMenu',
  'TNavBar',
  'TTabBar',
  // Dialog
  'TDialogProvider',
]);

export interface TnziUiResolverOptions {
  /** Component prefix */
  prefix?: string;
}

export function TnziUiResolver(options: TnziUiResolverOptions = {}): ComponentResolver {
  const { prefix = 'T' } = options;

  return {
    type: 'component',
    resolve: (name) => {
      if (name.startsWith(prefix) && validComponents.has(name)) {
        return {
          name: name,
          from: '@tnzi/shadcn',
        };
      }
      return undefined;
    },
  };
}
