/**
 * `useConfirm` + `TConfirmHost`, mounted for real: the answer each way, the
 * dialog staying open and busy while `action` runs, a failed `action` shown in
 * the dialog instead of closing it, Escape (dismisses; held while busy), focus
 * going back to the opener, the extra checkbox, and one host drawing a
 * confirmer when two are mounted.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TConfirmHost } = await import('../../src/components/overlay/TConfirmHost.vue');
const { createConfirmer, useConfirm } = await import('../../src/components/overlay/confirm');
const { en } = await import('../../src/locales/en');
const { zhCn } = await import('../../src/locales/zh-cn');
const { createAiI18n } = await import('../../src/i18n/index');

type Confirmer = ReturnType<typeof createConfirmer>;

let apps: App[] = [];
let trigger: HTMLButtonElement | null = null;

function mountHost(confirmer: Confirmer, setup?: () => void): void {
  const host = document.createElement('div');
  document.body.appendChild(host);
  const app = createApp({
    setup() {
      setup?.();
      return () => h(TConfirmHost, { confirmer });
    },
  });
  app.mount(host);
  apps.push(app);
}

function focusTrigger(): HTMLButtonElement {
  trigger = document.createElement('button');
  trigger.textContent = 'Delete';
  document.body.appendChild(trigger);
  trigger.focus();
  return trigger;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}
const afterLeave = () => new Promise((resolve) => setTimeout(resolve, 50));

const q = <T extends Element = HTMLElement>(selector: string) => document.querySelector<T>(selector);
const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

/** A promise that settles when the test says so. */
function deferred(): { promise: Promise<void>; resolve: () => void; reject: (error: unknown) => void } {
  let resolve!: () => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

afterEach(() => {
  for (const app of apps) app.unmount();
  apps = [];
  trigger?.remove();
  trigger = null;
  document.body.innerHTML = '';
  document.body.style.overflow = '';
});

describe('TConfirmHost', () => {
  it('shows an alertdialog with the title, the message and the two buttons', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    void confirmer.confirm({ title: 'Delete project?', message: 'It cannot be undone.', confirmText: 'Delete', danger: true });
    await settle();
    const dialog = q('[role="alertdialog"]')!;
    expect(dialog).not.toBeNull();
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe('Delete project?');
    expect(q('.t-confirm__message')?.textContent).toBe('It cannot be undone.');
    expect(q('[data-confirm-cancel]')?.textContent?.trim()).toBe(en.common.cancel);
    const ok = q('[data-confirm-ok]')!;
    expect(ok.textContent?.trim()).toBe('Delete');
    expect(ok.className).toContain('t-button--danger');
  });

  it('resolves true on confirm and false on cancel, closing either way', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const yes = confirmer.confirm({ title: 'A', message: 'm' });
    await settle();
    expect(q('[data-confirm-ok]')?.className).toContain('t-button--primary');
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    expect(await yes).toBe(true);
    await settle();
    await afterLeave();
    expect(q('[role="alertdialog"]')).toBeNull();

    const no = confirmer.confirm({ title: 'B', message: 'm' });
    await settle();
    q<HTMLButtonElement>('[data-confirm-cancel]')!.click();
    expect(await no).toBe(false);
  });

  it('stays open and busy while the action runs, then closes when it succeeds', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const work = deferred();
    let checkedSeen: boolean | null = null;
    const answer = confirmer.confirm({
      title: 'Delete',
      message: 'm',
      action: (checked) => {
        checkedSeen = checked;
        return work.promise;
      },
    });
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    await settle();
    expect(q('[role="alertdialog"]')?.getAttribute('aria-busy')).toBe('true');
    expect(q<HTMLButtonElement>('[data-confirm-ok]')!.disabled).toBe(true);
    expect(q<HTMLButtonElement>('[data-confirm-cancel]')!.disabled).toBe(true);
    expect(q<HTMLButtonElement>('.t-dialog__close')!.disabled).toBe(true);
    expect(checkedSeen).toBe(false);

    work.resolve();
    expect(await answer).toBe(true);
    await settle();
    await afterLeave();
    expect(q('[role="alertdialog"]')).toBeNull();
  });

  it('does not close when the action fails: it shows the error in the dialog and can be tried again', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    let attempts = 0;
    let settled = false;
    const answer = confirmer.confirm({
      title: 'Delete',
      message: 'm',
      action: async () => {
        attempts += 1;
        if (attempts === 1) throw new Error('The server is busy');
      },
    });
    void answer.then(() => (settled = true));
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    await settle();
    expect(q('[role="alertdialog"]')).not.toBeNull();
    expect(settled).toBe(false);
    const notice = q('.t-notice')!;
    expect(notice.getAttribute('role')).toBe('alert');
    expect(notice.textContent).toContain('The server is busy');
    expect(q<HTMLButtonElement>('[data-confirm-ok]')!.disabled).toBe(false);

    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    expect(await answer).toBe(true);
    expect(attempts).toBe(2);
  });

  it('words a failure with `describe` when given', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    void confirmer.confirm({
      title: 'Delete',
      message: 'm',
      action: () => Promise.reject({ code: 'IN_USE' }),
      describe: (error) => `Still in use (${(error as { code: string }).code})`,
    });
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    await settle();
    expect(q('.t-notice')?.textContent).toContain('Still in use (IN_USE)');
  });

  it('dismisses on Escape (false), but not while the action runs', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const dismissed = confirmer.confirm({ title: 'A', message: 'm' });
    await settle();
    escape();
    expect(await dismissed).toBe(false);
    await settle();
    await afterLeave();

    const work = deferred();
    let settled = false;
    const busy = confirmer.confirm({ title: 'B', message: 'm', action: () => work.promise });
    void busy.then(() => (settled = true));
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    await settle();
    escape();
    await settle();
    expect(settled).toBe(false);
    expect(q('[role="alertdialog"]')).not.toBeNull();
    work.resolve();
    expect(await busy).toBe(true);
  });

  it('moves focus into the dialog and gives it back to the opener when it closes', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const opener = focusTrigger();
    const answer = confirmer.confirm({ title: 'A', message: 'm' });
    await settle();
    expect(q('[role="alertdialog"]')!.contains(document.activeElement)).toBe(true);
    escape();
    await answer;
    await settle();
    await afterLeave();
    expect(document.activeElement).toBe(opener);
  });

  it('gives focus back to the opener after a failed then cancelled action', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const opener = focusTrigger();
    const answer = confirmer.confirm({ title: 'A', message: 'm', action: () => Promise.reject(new Error('no')) });
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    await settle();
    q<HTMLButtonElement>('[data-confirm-cancel]')!.click();
    expect(await answer).toBe(false);
    await settle();
    await afterLeave();
    expect(document.activeElement).toBe(opener);
  });

  it('passes the extra checkbox to the action', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    let seen: boolean | null = null;
    const answer = confirmer.confirm({
      title: 'Delete',
      message: 'm',
      checkbox: 'Also delete its files',
      action: async (checked) => {
        seen = checked;
      },
    });
    await settle();
    const box = q<HTMLInputElement>('[data-confirm-check]')!;
    expect(box.closest('label')?.textContent).toContain('Also delete its files');
    box.click();
    await settle();
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    expect(await answer).toBe(true);
    expect(seen).toBe(true);
  });

  it('answers false to an unanswered request that a second one replaces', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    await settle();
    const first = confirmer.confirm({ title: 'First', message: 'm' });
    const second = confirmer.confirm({ title: 'Second', message: 'm' });
    expect(await first).toBe(false);
    await settle();
    expect(document.getElementById(q('[role="alertdialog"]')!.getAttribute('aria-labelledby')!)?.textContent).toBe('Second');
    q<HTMLButtonElement>('[data-confirm-ok]')!.click();
    expect(await second).toBe(true);
  });

  it('draws a confirmer in one host only when two are mounted', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer);
    mountHost(confirmer);
    await settle();
    void confirmer.confirm({ title: 'A', message: 'm' });
    await settle();
    expect(document.querySelectorAll('[role="alertdialog"]').length).toBe(1);
  });

  it('speaks the provided catalogue', async () => {
    const confirmer = createConfirmer();
    mountHost(confirmer, () => createAiI18n(zhCn));
    await settle();
    void confirmer.confirm({ title: 'A', message: 'm' });
    await settle();
    expect(q('[data-confirm-cancel]')?.textContent?.trim()).toBe(zhCn.common.cancel);
    expect(q('[data-confirm-ok]')?.textContent?.trim()).toBe(zhCn.common.confirm);
  });

  it('useConfirm talks to the confirmer a default host shows', async () => {
    const host = document.createElement('div');
    document.body.appendChild(host);
    const app = createApp({ render: () => h(TConfirmHost) });
    app.mount(host);
    apps.push(app);
    await settle();
    const answer = useConfirm()({ title: 'Default', message: 'm' });
    await settle();
    expect(q('[role="alertdialog"]')).not.toBeNull();
    q<HTMLButtonElement>('[data-confirm-cancel]')!.click();
    expect(await answer).toBe(false);
  });
});
