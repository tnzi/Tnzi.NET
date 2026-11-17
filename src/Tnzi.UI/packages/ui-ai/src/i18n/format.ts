/**
 * Substitute `{name}` placeholders in a message.
 *
 * Several catalogue entries carry placeholders (`{count}`, `{seconds}`,
 * `{size}`); this is the one helper that fills them, so components do not each
 * hand-roll a `.replace()`. Unknown placeholders are left untouched, which
 * makes a typo visible instead of silently blanking the string.
 *
 * Apart from the engine (`./index`), which imports the English catalogue, so a component with no
 * catalogue of its own (`TImageViewer`, through `image-viewer-text.ts`) can fill a message.
 *
 * @example formatAiMessage(t.mcp.toolCount, { count: 12 }) // '12 tools'
 */
export function formatAiMessage(
  template: string,
  values: Readonly<Record<string, string | number>>,
): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    key in values ? String(values[key]) : match,
  );
}
