/**
 * Widget protocol for the Workbench layout system.
 *
 * A `WidgetDef` is a declarative descriptor that `TWorkbenchLayout` walks
 * to render a dashboard. Each widget is rendered through a generic
 * `<component :is="def.component">` so consumers can mix built-in
 * business widgets with their own app-specific components without
 * subclassing anything.
 *
 * Design tenets:
 *  1. **Declarative-first** — a workbench is an array of objects; no JSX/
 *     render-prop required. Consumers can persist the layout to JSON.
 *  2. **Responsive by default** — `span` accepts either a fixed 1..24
 *     number or a breakpoint object that mirrors naive-ui's NGrid
 *     `responsive`.
 *  3. **Lazy-loading friendly** — `component` accepts either a Component
 *     or a `() => Promise<Component>` (treated as a
 *     `defineAsyncComponent`).
 *  4. **Permission-aware** — a widget may declare a `permission` key. The
 *     HOST page filters the widget list by it before render (the admin
 *     Dashboard checks it against the signed-in user's permissions, super-user
 *     bypass + fail-open, mirroring the sidebar), so a widget the user can't
 *     use never mounts and never fires a doomed fetch. `@tnzi/ui` treats
 *     `permission` as opaque metadata — the filtering policy lives in the host.
 *  5. **Persistable order** — widget `id` is stable so the user-drag-to-
 *     reorder layout can be persisted to localStorage and restored.
 *
 * Sunk from `@tnzi/ui-admin/widgets/types.ts` in 0.2.x.
 */
import type { Component, InjectionKey } from 'vue'

/**
 * NGrid-friendly responsive span. `xs/sm/md/lg/xl` mirror the naive-ui
 * `responsive="screen"` breakpoints (576 / 768 / 992 / 1200 / 1600 px).
 * A plain number applies to every breakpoint.
 */
export type SpanValue =
  | number
  | {
      xs?: number
      sm?: number
      md?: number
      lg?: number
      xl?: number
    }

/**
 * Information injected into every widget by `TWidgetCard` via the
 * `WIDGET_CONTEXT_KEY` provide/inject channel. Widgets can `useWidget()`
 * to read their own metadata and the framework-supplied helpers.
 */
export interface WidgetContext {
  /** The widget id from the descriptor. */
  id: string
  /** Imperatively triggers the surrounding card's refresh flow. */
  refresh: () => void
  /** Surface a one-off error so the card can render its error slot. */
  reportError: (err: unknown) => void
  /**
   * Set the busy indicator on the surrounding card so async work
   * surfaces a spinner without each widget rolling its own NSpin.
   */
  setBusy: (busy: boolean) => void
  /**
   * Register a callback that the surrounding card fires when the user
   * clicks the refresh button. Returns a disposer that removes the
   * registration on unmount.
   */
  onRefresh: (cb: () => void | Promise<void>) => () => void
}

export interface WidgetDef {
  /**
   * Stable identifier — used as the Vue list key, the persisted-order
   * localStorage key, and the value emitted from drag-reorder events.
   * Convention: `<module>.<widget>` e.g. `ai.usage`, `identity.stats`.
   */
  id: string

  /**
   * Vue Component or a dynamic-import factory. Factories are wrapped
   * with `defineAsyncComponent` so unused widgets aren't bundled into
   * the initial chunk.
   */
  component: Component | (() => Promise<unknown>)

  /** Card title — i18n key (resolved via the translate prop) or raw text. */
  title?: string

  /** Optional iconify icon shown next to the title. */
  icon?: string

  /**
   * Responsive grid span (defaults to 24 = full width). Numbers map to
   * NGrid's 24-col grid; objects unlock per-breakpoint overrides.
   */
  span?: SpanValue

  /**
   * Fixed card height in pixels, or `'auto'` for content-driven height.
   * Default `'auto'` — charts that need a definite canvas size pass a
   * number so the inner echarts container has somewhere to draw.
   */
  height?: number | 'auto'

  /**
   * Props forwarded verbatim to the widget component. Use this when the
   * widget is pure-props-driven.
   */
  props?: Record<string, unknown>

  /**
   * Permission key — when set, the HOST page drops this widget from the deck
   * for users who lack the permission (the admin Dashboard filters here with a
   * super-user bypass + fail-open, like the sidebar). Opaque to `@tnzi/ui`;
   * omit for widgets that are always shown (business tiles, or mixed tiles that
   * degrade their own inaccessible rows to "—").
   */
  permission?: string

  /**
   * Backend module short name this widget's data belongs to (e.g. `'chat'`,
   * `'ai'`, `'storage'`). When set, the HOST page drops the widget unless the
   * backend host actually loaded that module (the admin Dashboard filters
   * against the `GET /admin/shell/modules` availability signal). Orthogonal
   * to `permission` — the module gate has NO super-user bypass, because an
   * unloaded module's endpoints 404 for the super admin too. Opaque to
   * `@tnzi/ui`; omit for widgets that don't fetch from an optional module.
   */
  module?: string

  /**
   * Show a refresh button on the card header that calls the widget's
   * `WidgetContext.refresh()`. Default `true`.
   */
  refreshable?: boolean

  /** Hide the card chrome — render the component bare. Default `false`. */
  bare?: boolean

  /**
   * When true and the layout mode is `'draggable'`, this widget is
   * pinned to its position and excluded from drag-reorder (useful for
   * the header banner). Default `false`.
   */
  pinned?: boolean
}

/**
 * Workbench configuration consumed by `TWorkbenchLayout`. The whole
 * shape is also accepted by adapter plugins so consumers can declare a
 * dashboard inline at app boot.
 */
export interface WorkbenchConfig {
  /**
   * Widget descriptors rendered in order. Optional — when omitted the admin
   * Workbench page falls back to `defaultWorkbenchWidgets()`, so a consumer can
   * opt into the bundled deck with just `{ layout: 'draggable' }`.
   */
  widgets?: WidgetDef[]
  /**
   * Layout mode. `'fixed'` renders the widgets in the supplied order;
   * `'draggable'` overlays VueDraggable so the user can re-order and
   * persists the order to localStorage.
   */
  layout?: 'fixed' | 'draggable'
  /**
   * localStorage key used by the draggable layout to persist the
   * user-customised widget order. Defaults to
   * `'tnzi-workbench-order'` — override per workbench when one shell
   * embeds multiple workbenches (rare).
   */
  persistKey?: string
  /**
   * NGrid x / y gap in pixels. Defaults to 16 (matches typical card
   * spacing).
   */
  xGap?: number
  yGap?: number
}

/**
 * Vue injection key for the widget context channel. Exported separately
 * so the matching `useWidget()` composable can `inject(WIDGET_CONTEXT_KEY)`.
 */
export const WIDGET_CONTEXT_KEY: InjectionKey<WidgetContext> = Symbol('tnzi-widget-context')
