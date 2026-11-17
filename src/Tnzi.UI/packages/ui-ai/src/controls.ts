/**
 * `@tnzi/ui-ai/controls` - the package's controls without the rest of it.
 *
 * The form controls, status pieces, list states (`TStateView`, `TEmptyState`,
 * `TTemplateRow`) and notes (`useToast`, `TToastHost`) of `components/controls`, plus the
 * structural pieces they are used with: the segmented control, the popover and
 * dropdown menus with their entries, and the tooltip directive. Everything
 * reachable from here imports only `vue` and `@iconify/vue` (and `uqr`, this
 * package's own dependency, which `TQrCode` loads on demand): no naive-ui, no
 * `@tnzi/ui`, no `@tnzi/core` (gate: `__tests__/conventions/controls-entry.test.ts`),
 * so a small app or a desktop window can build its pages from them without
 * those packages. The styles are in the package stylesheet (`style.css`).
 *
 * The same components are also exported from `@tnzi/ui-ai/components`.
 */
export * from './components/controls/index';
export { default as TSegmented } from './components/layout/TSegmented.vue';
export type { SegmentedOption, ElementAttrs } from './components/layout/page-toolbar';
export { default as TPopoverMenu } from './components/overlay/TPopoverMenu.vue';
export { default as TMenuPanel } from './components/overlay/TMenuPanel.vue';
export { default as TDropdownMenu } from './components/overlay/TDropdownMenu.vue';
export { default as TMenuItem } from './components/overlay/TMenuItem.vue';
export { default as TMenuSeparator } from './components/overlay/TMenuSeparator.vue';
export { default as TContextMenu } from './components/overlay/TContextMenu.vue';
export { default as TPopover } from './components/overlay/TPopover.vue';
export { vTooltip, createTooltip, TOOLTIP_DELAY_MS, TOOLTIP_SIDE_ATTRIBUTE, TOOLTIP_LONG_PRESS_MS, TOOLTIP_TOUCH_SHOW_MS } from './headless/tooltip';
export { useElementSize } from './headless/useElementSize';
export type { ElementSize, UseElementSizeOptions } from './headless/useElementSize';
export type { TooltipOptions, TooltipValue, TooltipController } from './headless/tooltip';
export { default as TSettingGroup } from './components/layout/TSettingGroup.vue';
export { default as TSettingRow } from './components/layout/TSettingRow.vue';
export { applyControlsTheme, controlsAccentVars, CONTROLS_ACCENT_VARS } from './theme/controls-theme';
export type { ControlsThemeMode, ControlsThemeOptions } from './theme/controls-theme';
export { CONTROLS_ICON_NAMES, disableRemoteIcons } from './controls-icons';
