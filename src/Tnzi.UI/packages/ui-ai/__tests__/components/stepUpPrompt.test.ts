// @vitest-environment node
/**
 * Lock: the settings pages that call `[RequireStepUp]` endpoints render the
 * re-authentication prompt.
 *
 * `useAccountSettings` closes the challenge -> verify -> replay loop through
 * core's `withStepUp` (tested in `__tests__/headless/useAccountSettings.test.ts`)
 * and exposes the built-in `StepUpPromptController` as `stepUp`. That is only
 * half of it: if no page renders the controller, a challenged write just waits
 * forever on a prompt nobody can see. SFCs have no mount coverage in this
 * package, so the wiring is checked on the source.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

function source(relative: string): string {
  return readFileSync(resolve(here, relative), 'utf8')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');
}

const PAGES_WITH_GATED_WRITES = [
  // suspendTwoFactor / disableTotp / beginTotp / confirmTotp
  '../../src/components/settings/TSecuritySettings.vue',
  // confirmEmailChange / confirmPhoneChange
  '../../src/components/settings/TAccountSettings.vue',
];

describe('step-up prompt wiring', () => {
  it.each(PAGES_WITH_GATED_WRITES)('%s renders TStepUpPrompt bound to controller.stepUp', (file) => {
    const text = source(file);
    expect(text).toMatch(/import TStepUpPrompt from '\.\/TStepUpPrompt\.vue'/);
    expect(text).toMatch(/<TStepUpPrompt[^>]*:prompt="controller\.stepUp"/);
  });

  it('TStepUpPrompt drives every controller action a user can take', () => {
    const text = source('../../src/components/settings/TStepUpPrompt.vue');
    for (const action of ['choose(', 'submitCode(', 'resendCode(', 'back(', 'cancel(']) {
      expect(text, `prompt.${action}`).toContain(`prompt.${action}`);
    }
  });

  // `verify()` settles only through the renderer's choose / submitCode /
  // cancel; closing the settings dialog mid-prompt unmounted it with the
  // prompt still open, leaving the calling write (and its `busy`) pending.
  it('TStepUpPrompt cancels an open prompt when it unmounts', () => {
    const text = source('../../src/components/settings/TStepUpPrompt.vue');
    expect(text).toMatch(/onBeforeUnmount\(\(\) => \{[\s\S]*?if \(props\.prompt\.open\) props\.prompt\.cancel\(\)/);
  });

  it('TStepUpPrompt reads its copy from the package catalogue, not literals', () => {
    const text = source('../../src/components/settings/TStepUpPrompt.vue');
    expect(text).toMatch(/useAiI18n\(\)/);
    // Every visible string goes through `t.settings.stepUp*`.
    expect(text).toMatch(/t\.settings\.stepUpTitle/);
    expect(text).toMatch(/t\.settings\.stepUpNoMethods/);
    expect(text).toMatch(/formatAiMessage\(t\.value\.settings\.stepUpCodeSentTo/);
  });

  it('the catalogue carries the step-up keys in both locales', async () => {
    const { en } = await import('../../src/locales/en');
    const { zhCn } = await import('../../src/locales/zh-cn');
    const keys = Object.keys(en.settings).filter((k) => k.startsWith('stepUp'));
    expect(keys.length).toBeGreaterThanOrEqual(15);
    for (const key of keys) {
      expect(zhCn.settings[key as keyof typeof zhCn.settings], key).toBeTruthy();
    }
  });
});
