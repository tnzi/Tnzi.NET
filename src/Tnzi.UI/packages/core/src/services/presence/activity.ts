/**
 * Presence auto-away activity reporter - client-driven, framework-agnostic (DOM-based).
 *
 * Tracks real user activity (mouse / keyboard / touch / scroll). On returning from idle it
 * POSTs `active:true` (the server flips the user back to Online and pushes to watchers). After
 * `idleMinutes` with no activity it POSTs `active:false` once (the server resolves the user to
 * Away and pushes). There is NO periodic heartbeat: the server derives Away lazily from the
 * last idle signal, so the reporter only fires on transitions.
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

export function createPresenceActivityReporter(
  options: PresenceActivityReporterOptions,
): PresenceActivityReporter {
  const api = usePresenceApi(options.client);
  const isEnabled = options.isEnabled ?? (() => true);

  let idleTimer: ReturnType<typeof setTimeout> | null = null;
  let idle = false;
  let started = false;
  let lastArm = 0;

  const canRun = () =>
    typeof window !== 'undefined' && typeof document !== 'undefined' && isEnabled();

  const armIdleTimer = () => {
    if (idleTimer) clearTimeout(idleTimer);
    lastArm = Date.now();
    const minutes = Math.max(1, options.getIdleMinutes());
    idleTimer = setTimeout(() => {
      if (!canRun() || idle) return;
      idle = true;
      // idle → server resolves the user to Away and pushes.
      void api.reportActivity(false).catch(() => undefined);
    }, minutes * 60_000);
  };

  const onActivity = () => {
    if (!canRun()) return;
    if (idle) {
      idle = false;
      // back from idle → server flips to Online and pushes; always re-arm immediately.
      void api.reportActivity(true).catch(() => undefined);
      armIdleTimer();
      return;
    }
    // Throttle: while already active, re-arm the idle timer at most once per second so a busy
    // pointer (mousemove/scroll/wheel fire per-pixel) doesn't churn clearTimeout/setTimeout.
    // 1s slack is negligible against a multi-minute idle threshold.
    if (Date.now() - lastArm < ACTIVITY_REARM_THROTTLE_MS) return;
    armIdleTimer();
  };

  return {
    start() {
      if (started || typeof window === 'undefined') return;
      started = true;
      idle = false;
      for (const ev of ACTIVITY_EVENTS) {
        window.addEventListener(ev, onActivity, { passive: true });
      }
      armIdleTimer();
    },
    stop() {
      if (!started) return;
      started = false;
      idle = false;
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
