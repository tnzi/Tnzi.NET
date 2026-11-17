/**
 * `TQrCode`: a value drawn as a scannable QR code. One SVG, black modules on a
 * white square in both themes, the quiet zone part of the drawing, the
 * encoder loaded on demand with a same-size blank box until then.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, ref, type App } from 'vue';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import TQrCode from '../../src/components/controls/TQrCode.vue';
import * as controls from '../../src/controls';

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

function mount(props: Record<string, unknown>) {
  const state = ref(props);
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(TQrCode, state.value as never) });
  app.mount(host);
  return { root: host, state };
}

const svgOf = (root: HTMLElement) => root.querySelector('svg[data-qr-code]') as SVGElement;
const pathOf = (root: HTMLElement) => svgOf(root).querySelector('path');
const drawn = (root: HTMLElement) => vi.waitFor(() => expect(pathOf(root)).not.toBeNull());

const URI = 'otpauth://totp/Tnzi:ada@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Tnzi';

describe('TQrCode', () => {
  it('is exported from @tnzi/ui-ai/controls', () => {
    expect(controls.TQrCode).toBe(TQrCode);
  });

  it('draws the value as black modules on a white square, named for assistive tech', async () => {
    const { root } = mount({ value: URI, label: 'Scan with your authenticator' });
    await drawn(root);
    const svg = svgOf(root);
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('Scan with your authenticator');
    expect(svg.getAttribute('aria-busy')).toBeNull();
    expect(svg.querySelector('rect')!.getAttribute('fill')).toBe('#fff');
    expect(pathOf(root)!.getAttribute('fill')).toBe('#000');
    expect(svg.getAttribute('width')).toBe('152');
  });

  it('keeps the four-module quiet zone inside the drawing', async () => {
    const { root } = mount({ value: URI, label: 'QR' });
    await drawn(root);
    const [, , w, h] = svgOf(root).getAttribute('viewBox')!.split(' ').map(Number);
    expect(w).toBe(h);
    expect(w).toBeGreaterThanOrEqual(25 + 8);
    const d = pathOf(root)!.getAttribute('d')!;
    const xs = [...d.matchAll(/M(\d+) (\d+)/g)].map((m) => Number(m[1]));
    const ys = [...d.matchAll(/M(\d+) (\d+)/g)].map((m) => Number(m[2]));
    expect(Math.min(...xs)).toBe(4);
    expect(Math.min(...ys)).toBe(4);
    expect(Math.max(...xs)).toBe(w! - 5);
    expect(Math.max(...ys)).toBe(h! - 5);
  });

  it('takes the size, margin and error correction it is given', async () => {
    const { root } = mount({ value: URI, label: 'QR', size: 200, margin: 2, ecc: 'L' });
    await drawn(root);
    expect(svgOf(root).getAttribute('width')).toBe('200');
    const d = pathOf(root)!.getAttribute('d')!;
    expect(Math.min(...[...d.matchAll(/M(\d+) /g)].map((m) => Number(m[1])))).toBe(2);
  });

  it('redraws when the value changes, and shows the blank box for an empty one', async () => {
    const { root, state } = mount({ value: URI, label: 'QR' });
    await drawn(root);
    const first = pathOf(root)!.getAttribute('d');
    state.value = { value: URI.replace('JBSWY3DP', 'KRSXG5CT'), label: 'QR' };
    await vi.waitFor(() => expect(pathOf(root)!.getAttribute('d')).not.toBe(first));
    state.value = { value: '', label: 'QR' };
    await nextTick();
    await vi.waitFor(() => expect(pathOf(root)).toBeNull());
    expect(svgOf(root).querySelector('rect')).not.toBeNull();
  });

  it('loads the encoder with import(), never statically', () => {
    const source = readFileSync(resolve(__dirname, '../../src/components/controls/TQrCode.vue'), 'utf8');
    expect(source).not.toMatch(/import\s[^;(]*from\s+'uqr'/);
    expect(source).toMatch(/import\('uqr'\)/);
  });
});
