import { describe, it, expect } from 'vitest';
import { isImeComposing } from '../../src/utils/keyboard';

describe('isImeComposing', () => {
  it('is true while the event reports a composition', () => {
    expect(isImeComposing({ isComposing: true, keyCode: 13 })).toBe(true);
  });

  it('is true for the keyCode 229 Enter that Safari sends after compositionend', () => {
    expect(isImeComposing({ isComposing: false, keyCode: 229 })).toBe(true);
  });

  it('is false for a plain Enter', () => {
    expect(isImeComposing({ isComposing: false, keyCode: 13 })).toBe(false);
  });
});
