/**
 * @tnzi/ui/adapters
 *
 * The @tnzi/core adapters that touch no component library: the runtime
 * adapter (router + storage) and the theme adapter (writes the document).
 * The message / dialog / notification / loading-bar adapters drive Naive UI
 * and live in `@tnzi/ui-admin`.
 */

export { createThemeAdapter } from './theme';
export { createRuntimeAdapter } from './create-runtime-adapter';
export type { VueRouterLike } from './create-runtime-adapter';
