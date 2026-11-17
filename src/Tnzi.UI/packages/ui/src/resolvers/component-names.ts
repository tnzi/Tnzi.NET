/**
 * Every `T*` component `@tnzi/ui` exports, for {@link TnziUiResolver}.
 *
 * The resolver runs inside the consumer's build config, where the `.vue`
 * barrel cannot be imported, so the names are listed here.
 * `__tests__/resolvers/TnziUiResolver.test.ts` fails when this list and the
 * components barrel disagree in either direction. Not re-exported from the
 * package root.
 */
export const TNZI_UI_COMPONENT_NAMES: ReadonlySet<string> = new Set([
  'T403', 'T404', 'T500',
  'TActivityFeed', 'TAppShell', 'TAuthLayout', 'TAvatar',
  'TBlankLayout',
  'TCenteredLayout', 'TConfirm', 'TContainer', 'TCountTo',
  'TDescriptions',
  'TEmpty', 'TErrorPage',
  'TFilterChips',
  'TGrid',
  'TLoadingBar',
  'TMetricBars',
  'TNoteCard',
  'TResult',
  'TSection', 'TSiteFooter', 'TSiteHeader', 'TSkeleton', 'TSvgIcon',
  'TWaveBg',
]);
