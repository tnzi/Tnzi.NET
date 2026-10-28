/* Inline widgets: an agent's `show_widget` call shown as a live, sandboxed HTML widget in the reply. */
export { default as TWidgetCall } from './TWidgetCall.vue';
export { default as TWidgetFrame } from './TWidgetFrame.vue';
export { createWidgetToolRenderers } from './widget-renderers';
export type { WidgetRendererOptions } from './widget-renderers';
export {
  describeWidget,
  isWidgetCallShown,
  parseWidgetInput,
  widgetFileName,
  WIDGET_MAX_HTML_LENGTH,
  WIDGET_MAX_TITLE_LENGTH,
  WIDGET_TOOL,
} from './widget-tool';
export type { WidgetInput } from './widget-tool';
export { createWidgetHost, widgetLinkUrl, widgetMessageText, WIDGET_MAX_MESSAGE_LENGTH } from './widget-host';
export type { WidgetHost, WidgetHostHandlers, WidgetHostOptions } from './widget-host';
export {
  composeWidgetDocument,
  injectIntoHead,
  MCP_APPS_PROTOCOL_VERSION,
  standaloneWidgetDocument,
  widgetHostContext,
} from './widget-document';
export type { WidgetHostContext } from './widget-document';
export { readWidgetTheme, watchWidgetTheme, widgetColorScheme, WIDGET_THEME_VARIABLES } from './widget-theme';
export type { WidgetColorScheme, WidgetTheme } from './widget-theme';
