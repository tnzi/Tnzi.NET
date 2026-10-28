/**
 * `@tnzi/ui-ai/widget` - inline widgets: an agent's `show_widget` call shown as a live,
 * sandboxed HTML widget in its reply.
 *
 * Its own entry so a chat product that does not show widgets does not carry the frame, the host
 * bridge and the in-frame bootstrap in its `components` / root imports.
 *
 * @example
 * ```ts
 * import { createWidgetToolRenderers } from '@tnzi/ui-ai/widget'
 *
 * defineChatApp({
 *   toolRenderers: {
 *     ...createWidgetToolRenderers({ runnerUrl: 'https://widgets.example.com/ai/widget-runner' }),
 *   },
 * })
 * ```
 */
export * from '../components/widget/index';
