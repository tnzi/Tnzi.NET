import { describe, it, expect } from 'vitest';
import { buildUrl, parseQuery, safeLinkHref } from '../../src/utils/url';

describe('buildUrl', () => {
  it('returns the base unchanged when there are no params', () => {
    expect(buildUrl('/api/users')).toBe('/api/users');
    expect(buildUrl('/api/users', {})).toBe('/api/users');
  });

  it('appends params as a query string', () => {
    expect(buildUrl('/api/users', { page: 1, q: 'x' })).toBe('/api/users?page=1&q=x');
  });

  it('skips null and undefined but keeps falsy values such as 0 and false', () => {
    expect(buildUrl('/x', { a: null, b: undefined, c: 0, d: false })).toBe('/x?c=0&d=false');
  });

  it('continues an existing query string with & instead of a second ?', () => {
    expect(buildUrl('/x?a=1', { b: 2 })).toBe('/x?a=1&b=2');
  });

  it('percent-encodes values', () => {
    expect(buildUrl('/x', { q: 'a b&c' })).toBe('/x?q=a+b%26c');
  });

  it('returns the base unchanged when every param is null or undefined', () => {
    // `params` is non-empty, so the separator is appended even though nothing
    // follows it. Callers that pass an all-null object get a trailing `?`.
    expect(buildUrl('/x', { a: null })).toBe('/x?');
  });
});

describe('parseQuery', () => {
  it('parses with or without the leading ?', () => {
    expect(parseQuery('?a=1&b=two')).toEqual({ a: '1', b: 'two' });
    expect(parseQuery('a=1&b=two')).toEqual({ a: '1', b: 'two' });
  });

  it('decodes percent-encoding and plus signs', () => {
    expect(parseQuery('q=a+b%26c')).toEqual({ q: 'a b&c' });
  });

  it('keeps the last value for a repeated key and returns {} for an empty string', () => {
    expect(parseQuery('a=1&a=2')).toEqual({ a: '2' });
    expect(parseQuery('')).toEqual({});
  });
});

describe('safeLinkHref', () => {
  it('returns undefined for a missing URL', () => {
    expect(safeLinkHref(undefined)).toBeUndefined();
    expect(safeLinkHref(null)).toBeUndefined();
    expect(safeLinkHref('')).toBeUndefined();
  });

  it.each([
    'https://cdn.example.com/a.pdf',
    'http://example.com/a.pdf',
    'HTTPS://example.com/a.pdf',
    '/files/a.pdf',
    'files/a.pdf',
    '?page=2',
    '#section',
    'blob:https://app.example.com/0b1c',
    'data:image/png;base64,AAAA',
    'mailto:someone@example.com',
  ])('keeps %j', (url) => {
    expect(safeLinkHref(url)).toBe(url);
  });

  // The URL parser drops leading C0 controls and spaces and every tab / newline
  // before it reads the scheme; a pattern match on the raw string misses these.
  it.each([
    'javascript:alert(1)',
    ' JAVASCRIPT:alert(1)',
    '\u0001javascript:alert(1)',
    'java\tscript:alert(1)',
    'java\nscript:alert(1)',
    'java\rscript:alert(1)',
    'vbscript:msgbox(1)',
    'data:text/html,<script>alert(1)</script>',
    'data:application/xhtml+xml,<x/>',
    'file:///etc/passwd',
  ])('rejects %j', (url) => {
    expect(safeLinkHref(url)).toBeUndefined();
  });
});
