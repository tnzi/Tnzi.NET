// @vitest-environment node
/**
 * Convention gate: the full-size image viewer is fetched on the first click.
 *
 * `TAttachmentImage` renders every image a message carries, in the chat barrel
 * and in the embeddable widgets. Its viewer is `TImageViewer` (focus trap,
 * scroll lock, phone back layer, the set's stepping). Most thumbnails are never
 * opened, so the viewer is reached
 * through `defineAsyncComponent(() => import(...))`. The size budgets cannot
 * guard this: size-limit inlines `import()`, so a static import would read the
 * same. This test does.
 */
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const file = resolve(dirname(fileURLToPath(import.meta.url)), '../../src/components/chat/TAttachmentImage.vue');
const source = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

describe('the attachment viewer is lazy', () => {
  it('does not import the viewer statically', () => {
    expect(source).not.toMatch(/^\s*import\s[^\n]*\bfrom\s+['"][^'"]*overlay\/TImageViewer\.vue['"]/m);
  });

  it('reaches it through a dynamic import', () => {
    expect(source).toMatch(/defineAsyncComponent\(\s*\(\)\s*=>\s*import\(\s*['"]\.\.\/overlay\/TImageViewer\.vue['"]\s*\)\s*\)/);
  });
});
