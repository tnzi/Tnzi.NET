import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useVoiceInput } from '../../src/composables/useVoiceInput';

describe('useVoiceInput', () => {
  // Capture the on* handlers and lang set by the composable
  const handlers = {
    onstart: null as (() => void) | null,
    onresult: null as ((e: { results: unknown }) => void) | null,
    onerror: null as ((e: { error: string }) => void) | null,
    onend: null as (() => void) | null,
  };
  let recognitionLang = 'en-US';

  beforeEach(() => {
    recognitionLang = 'en-US';
    // vitest 4: `new`-able mock must use a regular function (arrow impls are not constructors)
    const SpeechRecognition = vi.fn(function () { return {
      start: vi.fn(),
      stop: vi.fn(),
      continuous: false,
      interimResults: true,
      get lang() { return recognitionLang; },
      set lang(v: string) { recognitionLang = v; },
      get onstart() { return handlers.onstart; },
      set onstart(v) { handlers.onstart = v; },
      get onresult() { return handlers.onresult; },
      set onresult(v) { handlers.onresult = v; },
      get onerror() { return handlers.onerror; },
      set onerror(v) { handlers.onerror = v; },
      get onend() { return handlers.onend; },
      set onend(v) { handlers.onend = v; },
    }; });
    (globalThis as Record<string, unknown>).SpeechRecognition = SpeechRecognition;
  });

  afterEach(() => {
    delete (globalThis as Record<string, unknown>).SpeechRecognition;
    delete (globalThis as Record<string, unknown>).webkitSpeechRecognition;
  });

  describe('isSupported', () => {
    it('is true when SpeechRecognition API is present', () => {
      const { isSupported } = useVoiceInput();
      expect(isSupported.value).toBe(true);
    });

    it('is false when neither SpeechRecognition nor webkitSpeechRecognition exists', () => {
      delete (globalThis as Record<string, unknown>).SpeechRecognition;
      const { isSupported } = useVoiceInput();
      expect(isSupported.value).toBe(false);
    });
  });

  describe('start / stop', () => {
    it('start() registers all recognition handlers', () => {
      const { start } = useVoiceInput();
      start();
      expect(handlers.onstart).toBeDefined();
      expect(handlers.onresult).toBeDefined();
      expect(handlers.onerror).toBeDefined();
      expect(handlers.onend).toBeDefined();
    });

    it('stop() is safe when called before start()', () => {
      const { stop } = useVoiceInput();
      expect(() => stop()).not.toThrow();
    });
  });

  describe('isListening', () => {
    it('is false initially', () => {
      const { isListening } = useVoiceInput();
      expect(isListening.value).toBe(false);
    });

    // Regression: the returned refs used to be wrapped in `Object.freeze()`,
    // which made the underlying `_rawValue` non-writable, so the recogniser
    // callbacks below threw instead of updating state. Voice input was fully
    // dead in the browser while these assertions were commented out as an
    // alleged test-environment limitation.
    it('flips to true on onstart and back to false on onend', () => {
      const { isListening, start } = useVoiceInput();
      start();
      handlers.onstart?.();
      expect(isListening.value).toBe(true);
      handlers.onend?.();
      expect(isListening.value).toBe(false);
    });

    it('surfaces a denied-microphone message and stops listening', () => {
      const { isListening, error, start } = useVoiceInput();
      start();
      handlers.onstart?.();
      handlers.onerror?.({ error: 'not-allowed' });
      expect(error.value).toMatch(/Microphone access denied/);
      expect(isListening.value).toBe(false);
    });
  });

  describe('lang option', () => {
    it('sets recognition.lang to the provided option', () => {
      const { start } = useVoiceInput({ lang: 'fr-FR' });
      start();
      expect(recognitionLang).toBe('fr-FR');
    });

    it('defaults to en-US when no lang option is provided', () => {
      const { start } = useVoiceInput();
      start();
      expect(recognitionLang).toBe('en-US');
    });
  });
});
