import { ref, type Ref, onUnmounted } from 'vue';

// ---------------------------------------------------------------------------
// Web Speech API — local type subset
//
// `lib.dom.d.ts` ships partial Web Speech declarations whose presence varies
// across TS releases (`SpeechRecognitionEvent` / `SpeechRecognitionErrorEvent`
// in particular). Declaring the minimal shape we actually consume keeps this
// composable typecheck-safe regardless of the host lib version.
// ---------------------------------------------------------------------------

interface SpeechRecognitionAlternativeLike {
  readonly transcript: string;
  readonly confidence: number;
}

interface SpeechRecognitionResultLike {
  readonly isFinal: boolean;
  readonly length: number;
  readonly [index: number]: SpeechRecognitionAlternativeLike;
}

interface SpeechRecognitionResultListLike {
  readonly length: number;
  readonly [index: number]: SpeechRecognitionResultLike;
}

interface SpeechRecognitionEventLike extends Event {
  readonly results: SpeechRecognitionResultListLike;
  readonly resultIndex: number;
}

interface SpeechRecognitionErrorEventLike extends Event {
  readonly error: string;
  readonly message?: string;
}

interface SpeechRecognitionLike {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  onstart: (() => void) | null;
  onresult: ((event: SpeechRecognitionEventLike) => void) | null;
  onerror: ((event: SpeechRecognitionErrorEventLike) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

type SpeechRecognitionCtor = new () => SpeechRecognitionLike;

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface UseVoiceInputOptions {
  /** BCP 47 language tag (default: 'en-US'). */
  lang?: string;
  /** Continuous recognition mode (default: false). */
  continuous?: boolean;
  /** Callback invoked each time the recogniser delivers a result. */
  onResult?: (transcript: string, isFinal: boolean) => void;
}

export interface UseVoiceInputReturn {
  /** Whether the recogniser is currently listening. */
  isListening: Readonly<Ref<boolean>>;
  /** Whether the Web Speech API is available in this browser. */
  isSupported: Readonly<Ref<boolean>>;
  /** Error message if microphone access failed. */
  error: Readonly<Ref<string | null>>;
  /** Start voice recognition. No-op if not supported. */
  start: () => void;
  /** Stop voice recognition. */
  stop: () => void;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

function resolveRecognitionCtor(): SpeechRecognitionCtor | null {
  const g = globalThis as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return g.SpeechRecognition ?? g.webkitSpeechRecognition ?? null;
}

export function useVoiceInput(options: UseVoiceInputOptions = {}): UseVoiceInputReturn {
  const isListening = ref(false);
  const isSupported = ref(false);
  const error = ref<string | null>(null);

  let recognition: SpeechRecognitionLike | null = null;
  const Ctor = resolveRecognitionCtor();

  if (Ctor) {
    isSupported.value = true;
  }

  function start(): void {
    if (!Ctor) return;

    stop();

    recognition = new Ctor();
    recognition.continuous = options.continuous ?? false;
    recognition.interimResults = true;
    recognition.lang = options.lang ?? 'en-US';

    recognition.onstart = () => {
      isListening.value = true;
      error.value = null;
    };

    recognition.onresult = (event) => {
      const results = event.results;
      let transcript = '';
      for (let i = 0; i < results.length; i++) {
        transcript += results[i]?.[0]?.transcript ?? '';
      }
      const isFinal = results[0]?.isFinal ?? false;
      options.onResult?.(transcript, isFinal);
    };

    recognition.onerror = (event) => {
      isListening.value = false;
      if (event.error === 'not-allowed') {
        error.value = 'Microphone access denied. Please allow access in your browser.';
      }
      recognition = null;
    };

    recognition.onend = () => {
      isListening.value = false;
      recognition = null;
    };

    recognition.start();
  }

  function stop(): void {
    recognition?.stop();
    recognition = null;
  }

  onUnmounted(() => stop());

  return {
    isListening: Object.freeze(isListening),
    isSupported: Object.freeze(isSupported),
    error: Object.freeze(error),
    start,
    stop,
  };
}
