import { describe, expect, it } from 'vitest';
import { replaceTextRange, tokenBeforeCaret } from '../../src/components/chat/composer-suggestions';

describe('tokenBeforeCaret', () => {
  it('is the run of non-space characters that ends at the caret', () => {
    expect(tokenBeforeCaret('/rev', 4)).toEqual({ text: '/rev', start: 0, end: 4 });
    expect(tokenBeforeCaret('ask @ann about it', 8)).toEqual({ text: '@ann', start: 4, end: 8 });
    expect(tokenBeforeCaret('line one\n/com', 13)).toEqual({ text: '/com', start: 9, end: 13 });
  });

  it('is empty right after a space, at the start, and while a range is selected', () => {
    expect(tokenBeforeCaret('hello ', 6)).toEqual({ text: '', start: 6, end: 6 });
    expect(tokenBeforeCaret('hello', 0)).toEqual({ text: '', start: 0, end: 0 });
    expect(tokenBeforeCaret('/review', 1, 7)).toEqual({ text: '', start: 1, end: 1 });
  });

  it('stops at the caret, not at the end of the word', () => {
    expect(tokenBeforeCaret('/review now', 4)).toEqual({ text: '/rev', start: 0, end: 4 });
  });

  it('clamps a caret outside the text', () => {
    expect(tokenBeforeCaret('/ab', 99)).toEqual({ text: '/ab', start: 0, end: 3 });
  });
});

describe('replaceTextRange', () => {
  it('replaces the range and puts the caret after the insert', () => {
    expect(replaceTextRange('/rev please', 0, 4, '/review ')).toEqual({ text: '/review  please', caret: 8 });
    expect(replaceTextRange('ask @an', 4, 7, '@ann ')).toEqual({ text: 'ask @ann ', caret: 9 });
  });

  it('accepts a reversed or out-of-range span', () => {
    expect(replaceTextRange('abc', 3, 1, 'X')).toEqual({ text: 'aX', caret: 2 });
    expect(replaceTextRange('abc', -5, 99, 'X')).toEqual({ text: 'X', caret: 1 });
  });
});
