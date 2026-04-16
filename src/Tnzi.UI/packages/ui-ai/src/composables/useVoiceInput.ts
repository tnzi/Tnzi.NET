import { ref, type Ref, onUnmounted } from 'vue';

// ---------------------------------------------------------------------------
// Types
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

export function useVoiceInput(options: UseVoiceInputOptions = {}): UseVoiceInputReturn {
  const isListening = ref(false);
  const isSupported = ref(false);
  const error = ref<string | null>(null);

  let recognition: SpeechRecognition | null = null;

  const SpeechRecognition =
    (globalThis as unknown as { SpeechRecognition: typeof SpeechRecognition }).SpeechRecognition ??
    (globalThis as unknown as { webkitSpeechRecognition: typeof SpeechRecognition }).webkitSpeechRecognition;

  if (SpeechRecognition) {
    isSupported.value = true;
  }

  function start(): void {
    if (!SpeechRecognition) return;

    stop();

    recognition = new SpeechRecognition();
    recognition.continuous = options.continuous ?? false;
    recognition.interimResults = true;
    recognition.lang = options.lang ?? 'en-US';

    recognition.onstart = () => {
      isListening.value = true;
      error.value = null;
    };

    recognition.onresult = (event: SpeechRecognitionEvent) => {
      const transcript = Array.from(event.results as unknown as SpeechRecognitionResult[])
        .map((r) => (r as unknown as SpeechRecognitionAlternative[])[0]?.transcript)
        .join('');
      const isFinal = (event.results[0] as unknown as SpeechRecognitionResult).isFinal;
      options.onResult?.(transcript, isFinal);
    };

    recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
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
