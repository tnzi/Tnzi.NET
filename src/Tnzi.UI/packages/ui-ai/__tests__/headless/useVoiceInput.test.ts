import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope, ref } from 'vue';
import { useVoiceInput } from '../../src/headless/useVoiceInput';

/** One fake recogniser per `new`, so a test can drive an older session after a newer one started. */
interface FakeRecognition {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  onstart: (() => void) | null;
  onresult: ((e: { results: unknown }) => void) | null;
  onerror: ((e: { error: string }) => void) | null;
  onend: (() => void) | null;
  start: ReturnType<typeof vi.fn>;
  stop: ReturnType<typeof vi.fn>;
}

let created: FakeRecognition[] = [];
const latest = () => created[created.length - 1]!;

function result(transcript: string, isFinal = false) {
  return { results: [Object.assign([{ transcript, confidence: 1 }], { isFinal })] };
}

/** Lets the async permission lookup behind a `not-allowed` settle. */
const settle = () => new Promise((r) => setTimeout(r, 0));

function stubPermission(state: PermissionState | 'throws' | 'absent') {
  const permissions =
    state === 'absent'
      ? undefined
      : {
          query: vi.fn(async () => {
            if (state === 'throws') throw new TypeError('microphone is not a valid permission name');
            return { state };
          }),
        };
  vi.stubGlobal('navigator', { ...globalThis.navigator, permissions });
}

describe('useVoiceInput', () => {
  beforeEach(() => {
    created = [];
    // vitest 4: a `new`-able mock must be a regular function (arrow impls are not constructors)
    const SpeechRecognition = vi.fn(function (this: FakeRecognition) {
      const instance: FakeRecognition = {
        continuous: false,
        interimResults: false,
        lang: '',
        onstart: null,
        onresult: null,
        onerror: null,
        onend: null,
        start: vi.fn(),
        stop: vi.fn(),
      };
      created.push(instance);
      return instance;
    });
    vi.stubGlobal('SpeechRecognition', SpeechRecognition);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('isSupported', () => {
    it('is true when the API is present on a secure page that may use the microphone', () => {
      expect(useVoiceInput().isSupported.value).toBe(true);
    });

    it('is false when neither SpeechRecognition nor webkitSpeechRecognition exists', () => {
      vi.stubGlobal('SpeechRecognition', undefined);
      expect(useVoiceInput().isSupported.value).toBe(false);
    });

    it('is false on a page that is not a secure context (the browser refuses every attempt there)', () => {
      vi.stubGlobal('isSecureContext', false);
      expect(useVoiceInput().isSupported.value).toBe(false);
    });

    it('is false when the permissions policy turns the microphone off', () => {
      vi.stubGlobal('document', {
        ...globalThis.document,
        featurePolicy: { allowsFeature: (feature: string) => feature !== 'microphone' },
      });
      const voice = useVoiceInput();
      expect(voice.isSupported.value).toBe(false);
      voice.start();
      expect(created).toHaveLength(0);
    });
  });

  describe('start / stop', () => {
    it('start() registers all recognition handlers', () => {
      useVoiceInput().start();
      expect(latest().onstart).toBeTypeOf('function');
      expect(latest().onresult).toBeTypeOf('function');
      expect(latest().onerror).toBeTypeOf('function');
      expect(latest().onend).toBeTypeOf('function');
      expect(latest().start).toHaveBeenCalledOnce();
    });

    it('stop() is safe when called before start()', () => {
      expect(() => useVoiceInput().stop()).not.toThrow();
    });

    it('reports `unavailable` when the recogniser refuses to start', () => {
      vi.stubGlobal(
        'SpeechRecognition',
        vi.fn(function () {
          return {
            start: () => {
              throw new Error('InvalidStateError');
            },
            stop: vi.fn(),
          };
        }),
      );
      const voice = useVoiceInput();
      voice.start();
      expect(voice.error.value).toBe('unavailable');
      expect(voice.isListening.value).toBe(false);
    });

    it('stops with its scope (the composer unmounting)', () => {
      const scope = effectScope();
      scope.run(() => useVoiceInput().start());
      scope.stop();
      expect(latest().stop).toHaveBeenCalledOnce();
    });
  });

  describe('isListening', () => {
    it('is false initially', () => {
      expect(useVoiceInput().isListening.value).toBe(false);
    });

    // While the browser asks for the microphone nothing else on screen changes,
    // so the button must already read as recording.
    it('is true from the click, before the recogniser reports that it started', () => {
      const voice = useVoiceInput();
      voice.start();
      expect(voice.isListening.value).toBe(true);
    });

    // Regression: the returned refs used to be wrapped in `Object.freeze()`,
    // which made the underlying `_rawValue` non-writable, so the recogniser
    // callbacks below threw instead of updating state.
    it('stays true on onstart and goes back to false on onend', () => {
      const { isListening, start } = useVoiceInput();
      start();
      latest().onstart?.();
      expect(isListening.value).toBe(true);
      latest().onend?.();
      expect(isListening.value).toBe(false);
    });

    it('an earlier session ending does not switch off the one started after it', () => {
      const voice = useVoiceInput();
      voice.start();
      const first = latest();
      voice.start();
      latest().onstart?.();
      first.onend?.();
      first.onerror?.({ error: 'aborted' });
      expect(voice.isListening.value).toBe(true);
      expect(voice.error.value).toBeNull();
    });
  });

  describe('results', () => {
    it('delivers the transcript, and still delivers the result spoken before the user stopped', () => {
      const onResult = vi.fn();
      const voice = useVoiceInput({ onResult });
      voice.start();
      latest().onresult?.(result('hello'));
      voice.stop();
      expect(voice.isListening.value).toBe(false);
      latest().onresult?.(result('hello world', true));
      expect(onResult).toHaveBeenLastCalledWith('hello world', true);
    });
  });

  describe('errors', () => {
    it.each([
      ['audio-capture', 'no-microphone'],
      ['network', 'network'],
      ['no-speech', 'no-speech'],
      ['service-not-allowed', 'service-not-allowed'],
      ['language-not-supported', 'language-not-supported'],
      ['aborted', 'aborted'],
      ['bad-grammar', 'unavailable'],
    ])('reports %s as %s and stops listening', (raw, code) => {
      const voice = useVoiceInput();
      voice.start();
      latest().onstart?.();
      latest().onerror?.({ error: raw });
      expect(voice.error.value).toBe(code);
      expect(voice.isListening.value).toBe(false);
    });

    it.each([
      ['denied', 'mic-blocked'],
      ['prompt', 'mic-not-granted'],
      ['granted', 'service-not-allowed'],
      ['throws', 'mic-not-granted'],
      ['absent', 'mic-not-granted'],
    ] as const)('tells not-allowed apart by the microphone permission (%s -> %s)', async (state, code) => {
      stubPermission(state);
      const voice = useVoiceInput();
      voice.start();
      latest().onerror?.({ error: 'not-allowed' });
      await settle();
      expect(voice.error.value).toBe(code);
    });

    it('does not report no-speech or aborted for a session the user stopped', () => {
      const voice = useVoiceInput();
      voice.start();
      voice.stop();
      latest().onerror?.({ error: 'no-speech' });
      latest().onerror?.({ error: 'aborted' });
      expect(voice.error.value).toBeNull();
    });

    it('clears the error when recognition starts again', () => {
      const voice = useVoiceInput();
      voice.start();
      latest().onerror?.({ error: 'network' });
      expect(voice.error.value).toBe('network');
      voice.start();
      expect(voice.error.value).toBeNull();
    });

    it('a not-allowed answer that settles after a new attempt started is dropped', async () => {
      stubPermission('denied');
      const voice = useVoiceInput();
      voice.start();
      latest().onerror?.({ error: 'not-allowed' });
      voice.start();
      await settle();
      expect(voice.error.value).toBeNull();
    });
  });

  describe('lang option', () => {
    it('sets recognition.lang to the provided option', () => {
      useVoiceInput({ lang: 'fr-FR' }).start();
      expect(latest().lang).toBe('fr-FR');
    });

    it('defaults to en-US when no lang option is provided', () => {
      useVoiceInput().start();
      expect(latest().lang).toBe('en-US');
    });

    it('reads a ref or getter each time recognition starts', () => {
      const lang = ref('zh-CN');
      const voice = useVoiceInput({ lang: () => lang.value });
      voice.start();
      expect(latest().lang).toBe('zh-CN');
      lang.value = 'en-GB';
      voice.start();
      expect(latest().lang).toBe('en-GB');
    });
  });
});
