/**
 * Citation URLs come from data (a RAG source, a search hit), and Vue does not
 * sanitize `:href`: a `javascript:` citation ran on click. Both citation
 * components now bind only URLs `safeLinkHref` accepts.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';
import TCitation from '../../src/components/chat/TCitation.vue';
import TSourceCitations from '../../src/components/knowledge/TSourceCitations.vue';

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

const UNSAFE = ['javascript:alert(1)', 'java\tscript:alert(1)', 'data:text/html,<script>alert(1)</script>'];

describe('citation links', () => {
  it.each(UNSAFE)('TCitation renders %j as a plain card, not a link', async (href) => {
    const el = mount(() => h(TCitation, { title: 'Doc', href }));
    await nextTick();
    expect(el.querySelector('a')).toBeNull();
    expect(el.querySelector('.t-citation')).not.toBeNull();
  });

  it('TCitation keeps an https link', async () => {
    const el = mount(() => h(TCitation, { title: 'Doc', href: 'https://example.com/doc' }));
    await nextTick();
    expect(el.querySelector('a')?.getAttribute('href')).toBe('https://example.com/doc');
  });

  it('TSourceCitations binds no href for an unsafe URL and keeps a safe one', async () => {
    const el = mount(() =>
      h(TSourceCitations, {
        citations: [
          { title: 'Bad', url: UNSAFE[1]! },
          { title: 'Good', url: 'https://example.com/good' },
        ],
      }),
    );
    await nextTick();
    const links = Array.from(el.querySelectorAll('a'));
    expect(links).toHaveLength(2);
    expect(links[0]!.hasAttribute('href')).toBe(false);
    expect(links[1]!.getAttribute('href')).toBe('https://example.com/good');
  });
});
