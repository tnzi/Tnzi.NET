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
  'TActivityFeed', 'TAddressFields', 'TAppHeader', 'TAppShell', 'TAttachmentWall',
  'TAuthLayout', 'TAvatar',
  'TBlankLayout', 'TBreadcrumb', 'TButtonIcon',
  'TCaptcha', 'TCenteredLayout', 'TComboSelect', 'TConfirm', 'TContainer', 'TCountTo',
  'TDataList', 'TDescriptions', 'TDrawerShell',
  'TEmpty', 'TErrorPage',
  'TFilterChips', 'TForm', 'TFullScreen',
  'TGrid',
  'THint',
  'TImageCaptcha', 'TImageUpload',
  'TLangSwitch', 'TLoadingBar',
  'TMenu', 'TMenuToggler', 'TMetricBars', 'TModalShell', 'TMoneyInput',
  'TNavBar', 'TNoteCard',
  'TOverlayTheme',
  'TPinToggler',
  'TRelativeTime', 'TReloadButton', 'TResult',
  'TSchemaForm', 'TSearchForm', 'TSection', 'TSiteFooter', 'TSiteHeader', 'TSkeleton',
  'TSourceBadge', 'TStatCard', 'TStatToCards', 'TStatusBadge', 'TSvgIcon',
  'TTabBar', 'TTable', 'TThemeSchemaSwitch',
  'TUserCard',
  'TWaveBg', 'TWidgetCard', 'TWorkbenchLayout',
]);
