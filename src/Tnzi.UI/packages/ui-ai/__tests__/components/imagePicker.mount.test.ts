/**
 * `TImagePicker`: the picture is the button that picks another (named by
 * `changeLabel`, reachable by keyboard), a picked or dropped file is emitted,
 * the same file can be picked twice, the remove chip shows only with a picture
 * and not while busy, and busy refuses picks.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { TImagePicker } = await import('../../src/components/controls/index');

let app: App | null = null;

function mount(props: Record<string, unknown>): HTMLElement {
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () =>
      h(TImagePicker, { accept: 'image/png', changeLabel: 'Change picture', removeLabel: 'Remove picture', has: false, ...props }, () =>
        h('img', { class: 'picture', src: 'a.png', alt: '' }),
      ),
  });
  app.mount(host);
  return host;
}

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

describe('TImagePicker', () => {
  it('is a labelled button around the page picture that opens the file picker', () => {
    const root = mount({});
    const pick = root.querySelector<HTMLButtonElement>('[data-image-pick]')!;
    expect(pick.tagName).toBe('BUTTON');
    expect(pick.getAttribute('type')).toBe('button');
    expect(pick.getAttribute('aria-label')).toBe('Change picture');
    expect(pick.querySelector('.picture')).not.toBeNull();
    const input = root.querySelector<HTMLInputElement>('[data-image-file]')!;
    expect(input.accept).toBe('image/png');
    expect(input.tabIndex).toBe(-1);
    expect(input.getAttribute('aria-hidden')).toBe('true');
    const click = vi.spyOn(input, 'click');
    pick.click();
    expect(click).toHaveBeenCalled();
  });

  it('emits a picked file and clears the input so the same file counts again', async () => {
    const onPick = vi.fn();
    const root = mount({ onPick });
    const input = root.querySelector<HTMLInputElement>('[data-image-file]')!;
    const file = new File(['x'], 'a.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await nextTick();
    expect(onPick).toHaveBeenCalledWith(file);
    expect(input.value).toBe('');
  });

  it('takes a dropped file, marking the picture while a file is over it', async () => {
    const onPick = vi.fn();
    const root = mount({ onPick });
    const pick = root.querySelector<HTMLButtonElement>('[data-image-pick]')!;
    pick.dispatchEvent(new Event('dragover', { cancelable: true }));
    await nextTick();
    expect(pick.classList.contains('is-over')).toBe(true);
    const file = new File(['x'], 'b.png', { type: 'image/png' });
    const drop = new Event('drop', { cancelable: true }) as Event & { dataTransfer: unknown };
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [file] } });
    pick.dispatchEvent(drop);
    await nextTick();
    expect(onPick).toHaveBeenCalledWith(file);
    expect(pick.classList.contains('is-over')).toBe(false);
  });

  it('shows the remove chip only with a picture, named, emitting remove', async () => {
    expect(mount({}).querySelector('[data-image-remove]')).toBeNull();
    app!.unmount();
    document.body.innerHTML = '';
    const onRemove = vi.fn();
    const root = mount({ has: true, onRemove });
    const chip = root.querySelector<HTMLButtonElement>('[data-image-remove]')!;
    expect(chip.getAttribute('aria-label')).toBe('Remove picture');
    chip.click();
    await nextTick();
    expect(onRemove).toHaveBeenCalled();
  });

  it('refuses picks and hides the chip while busy', () => {
    const root = mount({ has: true, busy: true });
    const pick = root.querySelector<HTMLButtonElement>('[data-image-pick]')!;
    expect(pick.disabled).toBe(true);
    expect(pick.getAttribute('aria-busy')).toBe('true');
    expect(root.querySelector('[data-image-remove]')).toBeNull();
    expect(root.querySelector('.t-image-picker__busy')).not.toBeNull();
  });
});
