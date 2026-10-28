/**
 * Presence auto-away activity reporter - client-driven, framework-agnostic (DOM-based).
 *
 * Tracks real user activity (mouse / keyboard / touch / scroll). On returning from idle it
 * POSTs `active:true` (the server flips the user back to Online and pushes to watchers). After
 * `idleMinutes` with no activity it POSTs `active:false` once (the server resolves the user to
 * Away and pushes). There is NO periodic heartbeat: the server derives Away lazily from the
 * last idle signal, so the reporter only fires on transitions.
 *
 * ★ The server keeps ONE auto-away flag per user, but every tab runs its own reporter. Tabs of
 * the same origin therefore share two facts through `localStorage`:
 * - the time ANY tab last saw activity, so a background tab whose own timer runs out does not
 *   report idle while the user is busy in another tab;
 * - whether a tab has reported idle, so the tab the user comes back to reports active even
 *   though it never went idle itself.
 * The first activity after `start()` also reports active, which clears a flag left behind by a
 * tab that was closed while idle, or by another device. When web storage is unavailable
 * (private mode, blocked site data) each tab falls back to its own view.
 *
 * Mirrors the thin SignalR realtime clients (createChatSignalRClient / createSettingsRealtimeClient):
 * a start/stop factory the host app wires after login.
 */

import type { HttpClient } from '../../http/http';
import { usePresenceApi } from './api';

export interface PresenceActivityReporterOptions {
  client: HttpClient
  /** Idle threshold in minutes (read fresh each cycle so a runtime config change takes effect). */
  getIdleMinutes: () => number
  /** Whether reporting is currently enabled (presence on + auto-away on). Default: always on. */
  isEnabled?: () => boolean
}

export interface PresenceActivityReporter {
  start(): void
  stop(): void
}

const ACTIVITY_EVENTS = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll', 'wheel'] as const;
const ACTIVITY_REARM_THROTTLE_MS = 1000;

/** Epoch ms of the latest activity seen by any tab of this origin. */
const LAST_ACTIVITY_KEY = 'tnzi:presence:last-activity';
/** Present while a tab has reported the user idle and no tab has reported active since. */
const IDLE_REPORTED_KEY = 'tnzi:presence:idle-reported';

function readShared(key: string): number | null {
  try {
    const raw = window.localStorage.getItem(key);
    const value = raw === null ? Number.NaN : Number(raw);
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function writeShared(key: string, value: number | null): void {
  try {
    if (value === null) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, String(value));
  } catch {
    // Storage unavailable: this tab keeps working on its own view.
  }
}

export function createPresenceActivityReporter(
  options: PresenceActivityReporterOptions,
): PresenceActivityReporter {
  const api = usePresenceApi(options.client);
  const isEnabled = options.isEnabled ?? (() => true);

  let idleTimer: ReturnType<typeof setTimeout> | null = null;
  /** This tab believes the user is idle (it reported idle, or found another tab had). */
  let idle = false;
  /** This tab has told the server the user is active since `start()`. */
  let announced = false;
  let started = false;
  let lastArm = 0;

  const canRun = () =>
    typeof window !== 'undefined' && typeof document !== 'undefined' && isEnabled();

  const thresholdMs = () => Math.max(1, options.getIdleMinutes()) * 60_000;

  const armIdleTimer = (delayMs: number = thresholdMs()) => {
    if (idleTimer) clearTimeout(idleTimer);
    lastArm = Date.now();
    idleTimer = setTimeout(onIdleTimer, delayMs);
  };

  function onIdleTimer(): void {
    idleTimer = null;
    if (!canRun() || idle) return;
    // Another tab may have seen activity after this one did: wait out the rest of ITS window.
    const lastActivity = readShared(LAST_ACTIVITY_KEY);
    const remaining = lastActivity === null ? 0 : lastActivity + thresholdMs() - Date.now();
    if (remaining > 0) {
      armIdleTimer(remaining);
      return;
    }
    idle = true;
    // One tab tells the server; the others only learn the user is idle.
    if (readShared(IDLE_REPORTED_KEY) !== null) return;
    writeShared(IDLE_REPORTED_KEY, Date.now());
    // idle → server resolves the user to Away and pushes.
    void api.reportActivity(false).catch(() => undefined);
  }

  const reportActive = (now: number) => {
    idle = false;
    announced = true;
    writeShared(IDLE_REPORTED_KEY, null);
    writeShared(LAST_ACTIVITY_KEY, now);
    // back from idle → server flips to Online and pushes; always re-arm immediately.
    void api.reportActivity(true).catch(() => undefined);
    armIdleTimer();
  };

  const onActivity = () => {
    if (!canRun()) return;
    const now = Date.now();
    if (idle || !announced) {
      reportActive(now);
      return;
    }
    // Throttle: while already active, re-arm the idle timer at most once per second so a busy
    // pointer (mousemove/scroll/wheel fire per-pixel) doesn't churn clearTimeout/setTimeout or
    // web storage. 1s slack is negligible against a multi-minute idle threshold.
    if (now - lastArm < ACTIVITY_REARM_THROTTLE_MS) return;
    // Another tab reported the user idle while this one was not looking.
    if (readShared(IDLE_REPORTED_KEY) !== null) {
      reportActive(now);
      return;
    }
    writeShared(LAST_ACTIVITY_KEY, now);
    armIdleTimer();
  };

  return {
    start() {
      if (started || typeof window === 'undefined') return;
      started = true;
      idle = false;
      announced = false;
      for (const ev of ACTIVITY_EVENTS) {
        window.addEventListener(ev, onActivity, { passive: true });
      }
      armIdleTimer();
    },
    stop() {
      if (!started) return;
      started = false;
      idle = false;
      announced = false;
      if (idleTimer) {
        clearTimeout(idleTimer);
        idleTimer = null;
      }
      if (typeof window !== 'undefined') {
        for (const ev of ACTIVITY_EVENTS) {
          window.removeEventListener(ev, onActivity);
        }
      }
    },
  };
}
