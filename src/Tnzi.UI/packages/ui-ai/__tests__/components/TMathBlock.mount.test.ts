/**
 * `TMathBlock` asks KaTeX for MathML only. The HTML output needs `katex.css` to
 * position its glyph spans and hide the MathML copy written next to them; the
 * package never shipped that stylesheet, so a formula rendered as a run of
 * unpositioned glyphs beside a second, native rendering of itself.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, type App } from 'vue';

const { default: TMathBlock } = await import('../../src/components/streaming/TMathBlock.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('TMathBlock', () => {
  it('renders MathML only, which needs no stylesheet', async () => {
    const el = mount(() => h(TMathBlock, { expression: 'x^2 + 1' }));
    await vi.waitFor(() => expect(el.querySelector('math')).not.toBeNull());
    expect(el.querySelector('.katex-html')).toBeNull();
    expect(el.querySelectorAll('math')).toHaveLength(1);
  });
});
