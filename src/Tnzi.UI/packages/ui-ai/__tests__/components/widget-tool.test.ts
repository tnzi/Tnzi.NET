/**
 * The `show_widget` call as the client reads it, and the document a widget runs as: what is a
 * widget (and what falls back to the tool card), where the theme and the bridge go in the
 * model's HTML, and that nothing the host writes into the document can close its script.
 */
import { describe, expect, it } from 'vitest';
import {
  describeWidget,
  isWidgetCallShown,
  parseWidgetInput,
  widgetFileName,
  WIDGET_MAX_HTML_LENGTH,
  WIDGET_MAX_TITLE_LENGTH,
} from '../../src/components/widget/widget-tool';
import {
  composeWidgetDocument,
  injectIntoHead,
  standaloneWidgetDocument,
  widgetHostContext,
  widgetThemeStyle,
} from '../../src/components/widget/widget-document';
import { readWidgetTheme, sameWidgetTheme, WIDGET_THEME_VARIABLES, widgetColorScheme, type WidgetTheme } from '../../src/components/widget/widget-theme';
import { en } from '../../src/locales/en';
import type { ToolCallInfo } from '../../src/headless/useChat';

const HTML = '<!DOCTYPE html><html><head><title>x</title></head><body><input type="range"></body></html>';

describe('parseWidgetInput', () => {
  it('reads the arguments as an object or as JSON text', () => {
    expect(parseWidgetInput({ title: ' Usage ', html: HTML })).toEqual({ title: 'Usage', html: HTML });
    expect(parseWidgetInput(JSON.stringify({ title: 'Usage', html: HTML }))).toEqual({ title: 'Usage', html: HTML });
  });

  it('is null for anything that is not a usable widget', () => {
    expect(parseWidgetInput(null)).toBeNull();
    expect(parseWidgetInput('not json')).toBeNull();
    expect(parseWidgetInput([HTML])).toBeNull();
    expect(parseWidgetInput({ title: 'Usage' })).toBeNull();
    expect(parseWidgetInput({ title: '  ', html: HTML })).toBeNull();
    expect(parseWidgetInput({ title: 'Usage', html: '   ' })).toBeNull();
    expect(parseWidgetInput({ title: 'Usage', html: 42 })).toBeNull();
  });

  it('keeps the server limits', () => {
    expect(parseWidgetInput({ title: 't'.repeat(WIDGET_MAX_TITLE_LENGTH), html: HTML })).not.toBeNull();
    expect(parseWidgetInput({ title: 't'.repeat(WIDGET_MAX_TITLE_LENGTH + 1), html: HTML })).toBeNull();
    expect(parseWidgetInput({ title: 'Usage', html: 'h'.repeat(WIDGET_MAX_HTML_LENGTH + 1) })).toBeNull();
  });
});

describe('isWidgetCallShown', () => {
  const call = (patch: Partial<ToolCallInfo>): ToolCallInfo => ({ name: 'show_widget', input: { title: 'Usage', html: HTML }, ...patch });

  it('keeps a call in view while it is made and once it holds a widget', () => {
    expect(isWidgetCallShown(call({ status: 'running', input: undefined }))).toBe(true);
    expect(isWidgetCallShown(call({ status: 'completed' }))).toBe(true);
    expect(isWidgetCallShown(call({ isSuccess: true }))).toBe(true);
  });

  it('folds a call that failed or holds no usable document', () => {
    expect(isWidgetCallShown(call({ status: 'failed' }))).toBe(false);
    expect(isWidgetCallShown(call({ isSuccess: false, error: 'boom' }))).toBe(false);
    expect(isWidgetCallShown(call({ status: 'completed', input: { title: 'Usage' } }))).toBe(false);
  });
});

describe('describeWidget', () => {
  it('reads as a widget made, with its title as the subject', () => {
    const description = describeWidget({ name: 'show_widget', input: { title: 'Usage', html: HTML } }, en);
    expect(description.done).toBe(en.tools.widgetDone);
    expect(description.subject).toBe('Usage');
    expect(description.many?.(3)).toBe('Created 3 widgets');
  });
});

describe('widgetFileName', () => {
  it('makes the title safe for a file system', () => {
    expect(widgetFileName('Usage: 5h / weekly?')).toBe('Usage 5h weekly.html');
    expect(widgetFileName('...')).toBe('widget.html');
    expect(widgetFileName('用量')).toBe('用量.html');
  });
});

describe('injectIntoHead', () => {
  it('goes first in the head', () => {
    expect(injectIntoHead(HTML, '<X>')).toBe('<!DOCTYPE html><html><head><X><title>x</title></head><body><input type="range"></body></html>');
    expect(injectIntoHead('<html lang="en"><HEAD data-a="1"><style></style></HEAD></html>', '<X>')).toBe(
      '<html lang="en"><HEAD data-a="1"><X><style></style></HEAD></html>',
    );
  });

  it('does not mistake <header> for the head', () => {
    expect(injectIntoHead('<html><body><header>h</header></body></html>', '<X>')).toBe(
      '<html><head><X></head><body><header>h</header></body></html>',
    );
  });

  it('keeps a doctype first, and gives a fragment one so it is not rendered in quirks mode', () => {
    expect(injectIntoHead('<!doctype html><div>x</div>', '<X>')).toBe('<!doctype html><X><div>x</div>');
    expect(injectIntoHead('<div>x</div>', '<X>')).toBe('<!DOCTYPE html><X><div>x</div>');
  });
});

describe('the widget document', () => {
  const theme: WidgetTheme = { scheme: 'dark', variables: { '--color-text-primary': '#eee', '--font-sans': 'Inter, sans-serif' } };

  it('carries the theme before the widget CSS, and the bridge before the widget scripts', () => {
    const document = composeWidgetDocument(HTML, theme, widgetHostContext(theme, 'inline'));
    const style = document.indexOf('<style data-widget-theme>');
    const bridge = document.indexOf('<script>');
    expect(style).toBeGreaterThan(-1);
    expect(bridge).toBeGreaterThan(style);
    expect(bridge).toBeLessThan(document.indexOf('<title>x</title>'));
    expect(document).toContain('--color-text-primary:#eee;');
    expect(document).toContain('color-scheme:dark;');
    expect(document).toContain("'ui/initialize'");
    expect(document).toContain("'ui/notifications/size-changed'");
  });

  it('cannot be broken out of by what the host writes into it', () => {
    const hostile: WidgetTheme = { scheme: 'light', variables: { '--color-accent': 'red;}</style><script>alert(1)</script>' } };
    const document = composeWidgetDocument('<p>x</p>', hostile, widgetHostContext(hostile, 'inline', '</script><script>alert(2)'));
    expect(document.match(/<\/script>/g)).toHaveLength(1);
    expect(document.match(/<\/style>/g)).toHaveLength(1);
    expect(widgetThemeStyle(hostile)).not.toContain('}</style><script>');
  });

  it('has the theme but not the bridge when downloaded', () => {
    const copy = standaloneWidgetDocument(HTML, theme);
    expect(copy).toContain('<style data-widget-theme>');
    expect(copy).not.toContain('<script>');
  });
});

describe('the widget theme', () => {
  it('follows the dark class, and offers every variable the tool description names', () => {
    const page = document.createElement('div');
    page.className = 'dark';
    const inside = document.createElement('span');
    page.appendChild(inside);
    document.body.appendChild(page);
    try {
      expect(widgetColorScheme(inside)).toBe('dark');
      const theme = readWidgetTheme(inside);
      expect(theme.scheme).toBe('dark');
      for (const name of ['--color-text-primary', '--color-text-secondary', '--color-background-primary', '--color-background-secondary', '--color-border-primary', '--color-accent', '--font-sans', '--font-mono', '--border-radius-md']) {
        expect(WIDGET_THEME_VARIABLES).toContain(name);
        expect(theme.variables[name]).toBeTruthy();
      }
      expect(sameWidgetTheme(theme, readWidgetTheme(inside))).toBe(true);
      expect(sameWidgetTheme(theme, readWidgetTheme(document.body))).toBe(false);
    } finally {
      page.remove();
    }
  });
});
