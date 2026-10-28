import { describe, it, expect } from 'vitest';
import {
  normalizeCjkSpacing,
  stripInvisibleControlChars,
  normalizeTimeFormat,
} from '../../src/utils/markdown-normalizers';

describe('normalizeCjkSpacing', () => {
  it('adds space between Chinese and Latin word', () => {
    expect(normalizeCjkSpacing('你好world')).toBe('你好 world');
  });

  it('adds space between Latin and Chinese word', () => {
    expect(normalizeCjkSpacing('Hello世界')).toBe('Hello 世界');
  });

  it('does not double-space existing gaps', () => {
    expect(normalizeCjkSpacing('你好 world')).toBe('你好 world');
  });

  it('handles mixed CJK/Latin in same token', () => {
    expect(normalizeCjkSpacing('HelloWorld你好')).toBe('HelloWorld 你好');
  });

  it('spaces both sides of a Latin run inside CJK text', () => {
    expect(normalizeCjkSpacing('你好world世界')).toBe('你好 world 世界');
  });

  it('spaces CJK text from digits', () => {
    expect(normalizeCjkSpacing('共3条')).toBe('共 3 条');
  });

  it('is idempotent', () => {
    for (const input of ['你好world世界', 'world 你好', 'Hello世界', '共3条']) {
      const once = normalizeCjkSpacing(input);
      expect(normalizeCjkSpacing(once)).toBe(once);
    }
  });

  it('does not add a second space after an existing one', () => {
    expect(normalizeCjkSpacing('world 你好')).toBe('world 你好');
    expect(normalizeCjkSpacing('world\t你好')).toBe('world\t你好');
  });

  it('leaves CJK punctuation unspaced', () => {
    expect(normalizeCjkSpacing('你好。世界')).toBe('你好。世界');
    expect(normalizeCjkSpacing('你好，world')).toBe('你好，world');
  });

  it('leaves line breaks alone', () => {
    expect(normalizeCjkSpacing('你好\nworld')).toBe('你好\nworld');
  });

  it('keeps markdown emphasis around CJK text intact', () => {
    expect(normalizeCjkSpacing('**中文**text')).toBe('**中文**text');
    expect(normalizeCjkSpacing('`代码`')).toBe('`代码`');
  });

  it('passes through plain Latin text unchanged', () => {
    expect(normalizeCjkSpacing('Hello world')).toBe('Hello world');
  });
});

describe('stripInvisibleControlChars', () => {
  it('removes zero-width space', () => {
    expect(stripInvisibleControlChars('a\u200bb')).toBe('ab');
  });

  it('removes LTR and RTL marks', () => {
    expect(stripInvisibleControlChars('a\u200eb\u200fc')).toBe('abc');
  });

  it('removes word joiner', () => {
    expect(stripInvisibleControlChars('hello\u2060world')).toBe('helloworld');
  });

  it('removes soft hyphen', () => {
    expect(stripInvisibleControlChars('soft\u00ADhyphen')).toBe('softhyphen');
  });

  it('passes through plain text unchanged', () => {
    expect(stripInvisibleControlChars('Hello world!')).toBe('Hello world!');
  });

  it('is idempotent', () => {
    const input = 'a\u200bb\u200ec\u200bd';
    expect(stripInvisibleControlChars(stripInvisibleControlChars(input))).toBe(
      stripInvisibleControlChars(input),
    );
  });
});

describe('normalizeTimeFormat', () => {
  it('removes spaces around colons in time', () => {
    expect(normalizeTimeFormat('Meeting at 10 : 30')).toBe('Meeting at 10:30');
  });

  it('adds dash between AM/PM time ranges', () => {
    expect(normalizeTimeFormat('Hours: 9 AM to 5 PM')).toBe('Hours: 9 AM - 5 PM');
  });

  it('removes spaces inside parenthetical time ranges', () => {
    expect(normalizeTimeFormat('( 9:00 - 10:00 )')).toBe('(9:00-10:00)');
  });

  it('passes through well-formed ISO timestamp unchanged', () => {
    const input = '2026-04-16T10:30:00Z';
    expect(normalizeTimeFormat(input)).toBe(input);
  });

  it('handles 24-hour time ranges', () => {
    expect(normalizeTimeFormat('Shift: 08 : 00 to 16 : 30')).toBe('Shift: 08:00 to 16:30');
  });

  it('is idempotent', () => {
    const input = 'Hours 9 AM to 5 PM';
    expect(normalizeTimeFormat(normalizeTimeFormat(input))).toBe(normalizeTimeFormat(input));
  });
});
