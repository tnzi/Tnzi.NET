/**
 * Registering the widget renderer: `createWidgetToolRenderers({ runnerUrl })` gives the tool
 * renderer map entry for `show_widget` (also matched as `mcp__<server>__show_widget`), to spread
 * into `defineChatApp({ toolRenderers })` or `TChatApp`'s `toolRenderers`.
 *
 * ```ts
 * defineChatApp({
 *   toolRenderers: {
 *     ...createWidgetToolRenderers({ runnerUrl: 'https://widgets.example.com/ai/widget-runner' }),
 *   },
 * })
 * ```
 */
import { defineComponent, h, type PropType } from 'vue';
import type { ToolCallInfo } from '../../headless/useChat';
import type { ToolRendererMap } from '../chat/chat-render-context';
import TWidgetCall from './TWidgetCall.vue';
import type { WidgetHostHandlers } from './widget-host';
import { describeWidget, isWidgetCallShown, WIDGET_TOOL } from './widget-tool';

export interface WidgetRendererOptions {
  /**
   * The widget runner page: the server's `AI:Widgets:RunnerPath`, absolute (ideally on a host name
   * of its own) or relative to the chat page.
   */
  readonly runnerUrl: string;
  /** The tallest an inline widget grows, in pixels (640 by default); beyond it the widget scrolls. */
  readonly maxHeight?: number;
  /** What widgets may do beyond showing themselves (`ui/message`, `tools/call`, where links open). */
  readonly handlers?: WidgetHostHandlers;
  /** The tool's name, when a server declares it under another (default `show_widget`). */
  readonly toolName?: string;
}

/** The renderer entry for the widget tool. Throws when `runnerUrl` is empty: a widget would have nowhere to run. */
export function createWidgetToolRenderers(options: WidgetRendererOptions): ToolRendererMap {
  const runnerUrl = options.runnerUrl?.trim();
  if (!runnerUrl) throw new Error('createWidgetToolRenderers: runnerUrl is required (the server serves it at AI:Widgets:RunnerPath).');

  const component = defineComponent({
    name: 'TWidgetToolRenderer',
    props: {
      toolCall: { type: Object as PropType<ToolCallInfo>, required: true },
      streaming: { type: Boolean, default: false },
    },
    setup(props) {
      return () =>
        h(TWidgetCall, {
          toolCall: props.toolCall,
          streaming: props.streaming,
          runnerUrl,
          maxHeight: options.maxHeight,
          handlers: options.handlers,
        });
    },
  });

  return Object.freeze({
    [options.toolName ?? WIDGET_TOOL]: { component, describe: describeWidget, alwaysVisible: isWidgetCallShown },
  });
}
