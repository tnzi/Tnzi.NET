import { _api } from '@iconify/vue';

/**
 * Every Iconify name the components of `@tnzi/ui-ai/controls` and
 * `@tnzi/ui-ai/dialogs` render on their own (a select's chevron, a notice's
 * sign, a toast's close button, the image viewer's zoom buttons), for a page
 * that bundles its icons instead of letting `@iconify/vue` fetch them: add
 * these to the page's own names, register them, then `disableRemoteIcons()`
 * (below, also in `@tnzi/ui-ai/controls`).
 * README, "控件单独使用" has the recipe.
 *
 * Names a page passes in (`icon` props, menu items) are the page's own.
 * Gate: `__tests__/conventions/controls-icons.test.ts` holds this list to the
 * icon names written in the files the controls and dialogs entries reach.
 */
export const CONTROLS_ICON_NAMES: readonly string[] = [
  'lucide:arrow-down',
  'lucide:arrow-up',
  'lucide:camera',
  'lucide:check',
  'lucide:chevron-down',
  'lucide:chevron-left',
  'lucide:chevron-right',
  'lucide:circle-alert',
  'lucide:circle-check',
  'lucide:clock',
  'lucide:cloud-off',
  'lucide:ellipsis',
  'lucide:inbox',
  'lucide:info',
  'lucide:loader-circle',
  'lucide:pencil',
  'lucide:plus',
  'lucide:rotate-ccw',
  'lucide:settings-2',
  'lucide:trash-2',
  'lucide:triangle-alert',
  'lucide:x',
  'lucide:zoom-in',
  'lucide:zoom-out',
];

/**
 * Stops `@iconify/vue` from asking the public Iconify API for an icon it does
 * not have: an icon nobody registered renders blank instead of being fetched,
 * so no icon request leaves the page. Call it once, before mounting, after
 * registering the page's icons with `addCollection`. Only the default provider
 * is affected: one added with `addAPIProvider` keeps working.
 *
 * Here rather than in the generated manifest so a page built from the controls
 * alone can call it without `@tnzi/ui-ai/icons`, which brings `@iconify/utils`
 * and the package's whole icon-name manifest with it; that entry re-exports it.
 */
export function disableRemoteIcons(): void {
  _api.setAPIModule('', { prepare: () => [], send: () => {} });
}
