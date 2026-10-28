import { getCurrentScope, onScopeDispose, readonly, ref, toValue, type MaybeRefOrGetter, type Ref } from 'vue';

// ---------------------------------------------------------------------------
// Web Speech API - local type subset
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

/**
 * Why voice input stopped, one value per thing the user can do about it. A
 * component turns it into a sentence in the current language (`TThreadComposer`
 * reads `composer.voice*` from the package dictionary).
 *
 * - `mic-blocked`: the microphone is blocked for this site (the browser
 *   remembers a "Block"); the user changes it in the site settings.
 * - `mic-not-granted`: the microphone was refused without a remembered block:
 *   the permission prompt was dismissed, or the browser or the app hosting the
 *   page refused without asking.
 * - `service-not-allowed`: the microphone is allowed, but the browser does not
 *   allow its speech recognition service here.
 * - `no-microphone`: no microphone could be opened (none connected, or another
 *   app holds it).
 * - `network`: the speech service could not be reached. Browsers that expose
 *   the API without shipping the service (embedded webviews) fail this way.
 * - `no-speech`: nothing was heard before the recogniser gave up.
 * - `language-not-supported`: the service does not recognise `lang`.
 * - `aborted`: the browser cut the session short.
 * - `unavailable`: anything else, or the recogniser refused to start.
 */
export type VoiceInputErrorCode =
  | 'mic-blocked'
  | 'mic-not-granted'
  | 'service-not-allowed'
  | 'no-microphone'
  | 'network'
  | 'no-speech'
  | 'language-not-supported'
  | 'aborted'
  | 'unavailable';

export interface UseVoiceInputOptions {
  /** BCP 47 language tag (default: 'en-US'). Read each time recognition starts, so a ref or getter follows changes. */
  lang?: MaybeRefOrGetter<string | null | undefined>;
  /** Continuous recognition mode (default: false). */
  continuous?: boolean;
  /** Callback invoked each time the recogniser delivers a result. */
  onResult?: (transcript: string, isFinal: boolean) => void;
}

export interface UseVoiceInputReturn {
  /** Whether the recogniser is listening, or starting to (the browser may be asking for the microphone). */
  isListening: Readonly<Ref<boolean>>;
  /**
   * Whether voice input can work on this page: the browser has a recogniser, the
   * page is a secure context, and its permissions policy does not turn the
   * microphone off. A recogniser that exists but cannot reach a service is only
   * found out on use (`error` becomes `network`).
   */
  isSupported: Readonly<Ref<boolean>>;
  /** Why the last attempt stopped, or null. Cleared when recognition starts again. */
  error: Readonly<Ref<VoiceInputErrorCode | null>>;
  /** Start voice recognition. No-op if not supported. Call it from the click itself: the browser asks for the microphone then. */
  start: () => void;
  /** Stop voice recognition. The result spoken so far still arrives. */
  stop: () => void;
}

// ---------------------------------------------------------------------------
// Environment probes
// ---------------------------------------------------------------------------

function resolveRecognitionCtor(): SpeechRecognitionCtor | null {
  const g = globalThis as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor;
    webkitSpeechRecognition?: SpeechRecognitionCtor;
  };
  return g.SpeechRecognition ?? g.webkitSpeechRecognition ?? null;
}

interface PolicyLike {
  allowsFeature?: (feature: string) => boolean;
}

/**
 * Whether the page may use the microphone at all: a secure context, and no
 * permissions policy (a `Permissions-Policy: microphone=()` header, an iframe
 * without `allow="microphone"`) turning it off. On such a page the recogniser
 * fails every time with the same `not-allowed` a user's refusal gives, so the
 * button is not offered rather than offered and refused.
 */
function pageMayUseMicrophone(): boolean {
  const g = globalThis as unknown as {
    isSecureContext?: boolean;
    document?: { permissionsPolicy?: PolicyLike; featurePolicy?: PolicyLike };
  };
  if (g.isSecureContext === false) return false;
  const policy = g.document?.permissionsPolicy ?? g.document?.featurePolicy;
  try {
    return policy?.allowsFeature?.('microphone') ?? true;
  } catch {
    return true;
  }
}

type MicrophonePermission = 'granted' | 'denied' | 'prompt' | 'unknown';

/** The site's microphone permission, where the browser can say (not every one names `microphone`). */
async function queryMicrophonePermission(): Promise<MicrophonePermission> {
  const permissions = (globalThis as unknown as { navigator?: { permissions?: Permissions } }).navigator?.permissions;
  if (!permissions?.query) return 'unknown';
  try {
    const status = await permissions.query({ name: 'microphone' as PermissionName });
    return status.state;
  } catch {
    return 'unknown';
  }
}

/**
 * `not-allowed` is the same event for "you blocked the microphone" and "this
 * browser will not do it": only the permission state tells them apart, and the
 * two need opposite advice.
 */
async function classifyNotAllowed(): Promise<VoiceInputErrorCode> {
  const state = await queryMicrophonePermission();
  if (state === 'denied') return 'mic-blocked';
  if (state === 'granted') return 'service-not-allowed';
  return 'mic-not-granted';
}

const ERROR_CODES: Readonly<Record<string, VoiceInputErrorCode>> = {
  'service-not-allowed': 'service-not-allowed',
  'audio-capture': 'no-microphone',
  network: 'network',
  'no-speech': 'no-speech',
  'language-not-supported': 'language-not-supported',
  aborted: 'aborted',
};

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useVoiceInput(options: UseVoiceInputOptions = {}): UseVoiceInputReturn {
  const isListening = ref(false);
  const Ctor = resolveRecognitionCtor();
  const isSupported = ref(Ctor != null && pageMayUseMicrophone());
  const error = ref<VoiceInputErrorCode | null>(null);

  let recognition: SpeechRecognitionLike | null = null;
  /**
   * Bumped by every start. A recogniser's events count only while it is the
   * latest one: starting again before the previous session ended used to let
   * the old session's `end` switch the new one's state off.
   */
  let attempt = 0;
  /** The attempt the user stopped (its `no-speech` / `aborted` is not news to them). */
  let stoppedAttempt = -1;

  function report(raw: string, own: number): void {
    if (own === stoppedAttempt && (raw === 'no-speech' || raw === 'aborted')) return;
    if (raw === 'not-allowed') {
      void classifyNotAllowed().then((code) => {
        if (own === attempt) error.value = code;
      });
      return;
    }
    error.value = ERROR_CODES[raw] ?? 'unavailable';
  }

  function start(): void {
    if (!Ctor || !isSupported.value) return;

    stop();
    error.value = null;
    const own = ++attempt;
    const instance = new Ctor();
    recognition = instance;
    instance.continuous = options.continuous ?? false;
    instance.interimResults = true;
    instance.lang = toValue(options.lang) || 'en-US';

    instance.onstart = () => {
      if (own === attempt && recognition === instance) isListening.value = true;
    };

    instance.onresult = (event) => {
      if (own !== attempt) return;
      const results = event.results;
      let transcript = '';
      for (let i = 0; i < results.length; i++) {
        transcript += results[i]?.[0]?.transcript ?? '';
      }
      const isFinal = results[0]?.isFinal ?? false;
      options.onResult?.(transcript, isFinal);
    };

    instance.onerror = (event) => {
      if (own !== attempt) return;
      if (recognition === instance) recognition = null;
      isListening.value = false;
      report(event.error, own);
    };

    instance.onend = () => {
      if (own !== attempt) return;
      if (recognition === instance) recognition = null;
      isListening.value = false;
    };

    // Listening from the click, not from the recogniser's `start` event: the
    // browser may first ask for the microphone, and a button that did not react
    // meanwhile read as broken (and invited a second, overlapping start).
    isListening.value = true;
    try {
      instance.start();
    } catch {
      recognition = null;
      isListening.value = false;
      error.value = 'unavailable';
    }
  }

  function stop(): void {
    const instance = recognition;
    recognition = null;
    isListening.value = false;
    if (!instance) return;
    stoppedAttempt = attempt;
    instance.stop();
  }

  // Guarded: usable outside a component (a store, a test), where registering a
  // lifecycle hook would only warn.
  if (getCurrentScope()) onScopeDispose(stop);

  // `readonly()`, NOT `Object.freeze()`: freezing a ref makes its own
  // `_rawValue` / `_value` data properties non-writable, so every later
  // `isListening.value = …` from the recogniser callbacks throws
  // `TypeError: Cannot assign to read only property '_rawValue'` in strict
  // mode. `readonly()` returns a read-only PROXY and leaves the source ref
  // writable, which is what the `Readonly<Ref<…>>` return type means.
  return {
    isListening: readonly(isListening),
    isSupported: readonly(isSupported),
    error: readonly(error),
    start,
    stop,
  };
}
