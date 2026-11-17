/**
 * Controls - the form controls and small status pieces every page of an app
 * is built from: buttons, fields, dropdowns, switches, tags, notices.
 *
 * A *structural* domain, like `layout/` and `overlay/`: nothing here carries
 * AI semantics. These are also what `@tnzi/ui-ai/controls` serves (with the
 * segmented control, the menus and the tooltip from the other two structural
 * domains), and nothing in this folder may import naive-ui, `@tnzi/ui` or
 * `@tnzi/core` (gate: `__tests__/conventions/controls-entry.test.ts`), so that
 * entry stays usable without them.
 *
 * The native-element classes (`.t-input`, `.t-select`, `.t-check`, `.t-mono`)
 * are the global `controls.css`, part of the package stylesheet.
 */

export { default as TButton } from './TButton.vue';
export { default as TIconButton } from './TIconButton.vue';
export { default as TField } from './TField.vue';
export { default as TSelect } from './TSelect.vue';
export { default as TMultiSelect } from './TMultiSelect.vue';
/* Picks one out of many: a search box over the options, or over pages a loader fetches. */
export { default as TSearchSelect } from './TSearchSelect.vue';
export { createSearchSelectSource, searchSelectMatches, SEARCH_SELECT_DEBOUNCE_MS } from './search-select';
export type { TSearchSelectOption, SearchSelectLoader, SearchSelectLoadContext, SearchSelectPage, SearchSelectSource, SearchSelectSourceOptions } from './search-select';
export { default as TSwitch } from './TSwitch.vue';
export { default as TTag } from './TTag.vue';
export { default as TNotice } from './TNotice.vue';
export { default as TSpinner } from './TSpinner.vue';
export { default as TProgress } from './TProgress.vue';
/* A value as a scannable QR code, black on white in both themes. */
export { default as TQrCode } from './TQrCode.vue';
export { default as TSplitButton } from './TSplitButton.vue';
export { default as TBadge } from './TBadge.vue';
export { default as TInlineField } from './TInlineField.vue';
/* Supplementary information behind a button, reachable by a tap as by a mouse. */
export { default as TInfoTip } from './TInfoTip.vue';
/* Rows of data as a data client lays them out. */
export { default as TDataGrid } from './TDataGrid.vue';
export { nextDataGridSort, sortDataGridRows, DATA_GRID_MIN_COLUMN_WIDTH, DATA_GRID_KEY_STEP } from './data-grid';
export type { DataGridColumn, DataGridSort, DataGridSortDirection, DataGridCell } from './data-grid';
export type { TSelectOption, TMultiSelectOption } from './options';

/* States and feedback: a list's loading / error / empty states, an empty
   page's drawing and its templates, and short-lived notes. */
export { default as TStateView } from './TStateView.vue';
export { default as TEmptyState } from './TEmptyState.vue';
export { default as TEmptyArt } from './TEmptyArt.vue';
export { EMPTY_ART_NAMES } from './empty-art';
export type { EmptyArt } from './empty-art';
export { default as TTemplateRow } from './TTemplateRow.vue';
/* A picture as the control that changes it (an avatar, a project's image). */
export { default as TImagePicker } from './TImagePicker.vue';
export { default as TToastHost } from './TToastHost.vue';
export { useToast, createToaster, TOAST_DURATION_MS, TOAST_ERROR_DURATION_MS, TOAST_ACTION_DURATION_MS, TOAST_MAX_ACTIONS, TOAST_MAX_VISIBLE_WIDE, TOAST_MAX_VISIBLE_NARROW } from './toast';
export type { Toast, Toaster, ToastAction, ToastHandle, ToastItem, ToastOptions, ToastTone, ToastUpdate } from './toast';
