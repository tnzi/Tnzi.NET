/**
 * The document a widget runs as: the model's HTML with the host's theme and a small bridge
 * injected at the top of its `<head>`.
 *
 * The runner page shows this document in its inner frame as is (it is a plain MCP Apps sandbox
 * proxy), so everything widget-specific lives here, versioned with the client:
 *
 *   - the theme: the variables a widget is told to use (`--color-text-primary`, …) on `:root`,
 *     `color-scheme`, and quiet defaults for `body` (font, colour, transparent background) that
 *     the widget's own CSS overrides;
 *   - the bridge (`window.widget`): it speaks the MCP Apps messages to the host through the runner
 *     (`ui/initialize`, `ui/notifications/initialized`, `ui/notifications/size-changed`,
 *     `ui/open-link`, `ui/message`, `tools/call`), reports the document's height as it changes,
 *     sends link clicks to the host (the frame may not navigate or open windows itself) and
 *     applies theme changes (`ui/notifications/host-context-changed`).
 *
 * A downloaded copy (`standaloneWidgetDocument`) carries the theme but not the bridge.
 */
import type { WidgetTheme } from './widget-theme';

/** The MCP Apps protocol version this host speaks. */
export const MCP_APPS_PROTOCOL_VERSION = '2026-01-26';

/** The context the host hands a widget (the MCP Apps `hostContext`, the parts this host fills). */
export interface WidgetHostContext {
  readonly theme: WidgetTheme['scheme'];
  readonly styles: { readonly variables: Readonly<Record<string, string>> };
  readonly displayMode: 'inline' | 'fullscreen';
  readonly locale?: string;
}

/** The host context for a theme. */
export function widgetHostContext(theme: WidgetTheme, displayMode: WidgetHostContext['displayMode'], locale?: string): WidgetHostContext {
  return { theme: theme.scheme, styles: { variables: { ...theme.variables } }, displayMode, ...(locale ? { locale } : {}) };
}

/** A CSS value cannot close the style element or the rule it sits in. */
function cssValue(value: string): string {
  return value.replace(/[<>{};]/g, '');
}

/** The theme as a `<style>` element: variables on `:root`, then defaults any widget CSS overrides. */
export function widgetThemeStyle(theme: WidgetTheme): string {
  const variables = Object.entries(theme.variables)
    .filter(([name]) => /^--[a-z0-9-]+$/i.test(name))
    .map(([name, value]) => `${name}:${cssValue(value)};`)
    .join('');
  return (
    '<style data-widget-theme>' +
    `:root{${variables}color-scheme:${theme.scheme};}` +
    ':where(body){font-family:var(--font-sans);font-size:14px;line-height:1.5;color:var(--color-text-primary);background:transparent;}' +
    '</style>'
  );
}

/**
 * The bridge, run inside the widget's document before any of the widget's own scripts. Plain
 * ES2015 that only uses what every engine has, written to a string so the runner page needs no
 * build of its own. `__CONTEXT__` is replaced with the initial host context (JSON).
 */
const BRIDGE_SOURCE = `(function (initial) {
  'use strict';
  var host = window.parent;
  var nextId = 1;
  var pending = {};
  var lastHeight = -1;
  var scheduled = false;
  function send(message) { host.postMessage(message, '*'); }
  function notify(method, params) { send({ jsonrpc: '2.0', method: method, params: params || {} }); }
  function request(method, params) {
    var id = 'widget-' + nextId++;
    return new Promise(function (resolve, reject) {
      pending[id] = { resolve: resolve, reject: reject };
      send({ jsonrpc: '2.0', id: id, method: method, params: params || {} });
    });
  }
  function applyContext(context) {
    if (!context || typeof context !== 'object') return;
    var root = document.documentElement;
    var variables = context.styles && context.styles.variables;
    if (variables && typeof variables === 'object') {
      Object.keys(variables).forEach(function (name) {
        if (/^--[a-z0-9-]+$/i.test(name) && typeof variables[name] === 'string') root.style.setProperty(name, variables[name]);
      });
    }
    if (context.theme === 'light' || context.theme === 'dark') {
      root.style.colorScheme = context.theme;
      root.setAttribute('data-theme', context.theme);
    }
    api.hostContext = context;
    try { window.dispatchEvent(new CustomEvent('widget:hostcontext', { detail: context })); } catch (e) { /* old engine */ }
  }
  function measure() {
    var root = document.documentElement;
    var height = root.getBoundingClientRect().height;
    if (document.body) height = Math.max(height, document.body.scrollHeight);
    return Math.ceil(height);
  }
  function report() {
    scheduled = false;
    var height = measure();
    if (height === lastHeight) return;
    lastHeight = height;
    notify('ui/notifications/size-changed', { width: Math.ceil(document.documentElement.getBoundingClientRect().width), height: height });
  }
  function schedule() {
    if (scheduled) return;
    scheduled = true;
    (window.requestAnimationFrame || setTimeout)(report);
  }
  function openLink(href) {
    var url;
    try { url = new URL(String(href)); } catch (e) { return Promise.reject(new Error('Not an absolute link: ' + href)); }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return Promise.reject(new Error('Only http and https links open'));
    return request('ui/open-link', { url: url.href });
  }
  var api = {
    hostContext: initial,
    openLink: openLink,
    sendMessage: function (text) { return request('ui/message', { role: 'user', content: [{ type: 'text', text: String(text) }] }); },
    callTool: function (name, args) { return request('tools/call', { name: String(name), arguments: args || {} }); }
  };
  window.widget = api;
  window.addEventListener('message', function (event) {
    if (event.source !== host) return;
    var data = event.data;
    if (!data || typeof data !== 'object' || data.jsonrpc !== '2.0') return;
    if (data.method === undefined && data.id !== undefined) {
      var waiting = pending[data.id];
      if (!waiting) return;
      delete pending[data.id];
      if (data.error) waiting.reject(new Error((data.error && data.error.message) || 'The host refused'));
      else waiting.resolve(data.result);
      return;
    }
    if (data.method === 'ui/notifications/host-context-changed') { applyContext(data.params); schedule(); return; }
    if (data.method === 'ui/notifications/tool-input') { api.toolInput = data.params && data.params.arguments; return; }
    if (data.method === 'ui/resource-teardown' && data.id !== undefined) { send({ jsonrpc: '2.0', id: data.id, result: {} }); return; }
    if (data.id !== undefined) send({ jsonrpc: '2.0', id: data.id, error: { code: -32601, message: 'Method not found' } });
  });
  document.addEventListener('click', function (event) {
    var target = event.target;
    var link = target && target.closest ? target.closest('a[href]') : null;
    if (!link) return;
    var href = link.getAttribute('href') || '';
    if (href.charAt(0) === '#') return;
    event.preventDefault();
    openLink(link.href && typeof link.href === 'string' ? link.href : href).catch(function () {});
  }, true);
  window.open = function (href) { if (href) openLink(href).catch(function () {}); return null; };
  applyContext(initial);
  request('ui/initialize', {
    protocolVersion: '${MCP_APPS_PROTOCOL_VERSION}',
    appInfo: { name: 'tnzi-widget', version: '1' },
    appCapabilities: {}
  }).then(function (result) {
    if (result && result.hostContext) applyContext(result.hostContext);
  }, function () {}).then(function () { notify('ui/notifications/initialized', {}); });
  function start() {
    schedule();
    if (window.ResizeObserver) {
      var observer = new ResizeObserver(schedule);
      observer.observe(document.documentElement);
      if (document.body) observer.observe(document.body);
    }
    window.addEventListener('load', schedule);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})(__CONTEXT__);`;

/** JSON that cannot close the script element it is written into. */
function scriptJson(value: unknown): string {
  return JSON.stringify(value)
    .replace(/</g, '\\u003c')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
}

/** The bridge as a `<script>` element, starting from `context`. */
export function widgetBridgeScript(context: WidgetHostContext): string {
  return `<script>${BRIDGE_SOURCE.replace('__CONTEXT__', () => scriptJson(context))}</script>`;
}

/**
 * Inserts `injected` as the first thing in the document's head: after `<head>` when there is one,
 * else after `<html>` (in a head of its own), else after the doctype, else at the start (with a
 * doctype in front, so the document is not rendered in quirks mode).
 */
export function injectIntoHead(html: string, injected: string): string {
  const head = /<head(?:\s[^>]*)?>/i.exec(html);
  if (head) return html.slice(0, head.index + head[0].length) + injected + html.slice(head.index + head[0].length);
  const root = /<html(?:\s[^>]*)?>/i.exec(html);
  if (root) return html.slice(0, root.index + root[0].length) + `<head>${injected}</head>` + html.slice(root.index + root[0].length);
  const doctype = /^\s*<!doctype[^>]*>/i.exec(html);
  if (doctype) return doctype[0] + injected + html.slice(doctype[0].length);
  return `<!DOCTYPE html>${injected}${html}`;
}

/** The document the runner shows: the widget with the theme and the bridge. */
export function composeWidgetDocument(html: string, theme: WidgetTheme, context: WidgetHostContext): string {
  return injectIntoHead(html, widgetThemeStyle(theme) + widgetBridgeScript(context));
}

/** A copy of the widget to keep or open on its own: the theme, without the bridge. */
export function standaloneWidgetDocument(html: string, theme: WidgetTheme): string {
  return injectIntoHead(html, widgetThemeStyle(theme));
}
