/**
 * @tnzi/core/adapters/event-bus
 *
 * Event bus for cross-module communication.
 */

import { useLogger } from '../logger';
import { createAdapterSingleton } from '../singleton';

// ============================================
// Type Definitions
// ============================================

/**
 * Event handler type.
 */
export type EventHandler<T = unknown> = (payload: T) => void;

/**
 * Unsubscribe function type.
 */
export type Unsubscribe = () => void;

/**
 * Event bus interface.
 */
export interface EventBus {
  /**
   * Subscribe to an event.
   */
  on<T = unknown>(event: string, handler: EventHandler<T>): Unsubscribe;

  /**
   * Subscribe to an event once (unsubscribes after first trigger).
   */
  once<T = unknown>(event: string, handler: EventHandler<T>): Unsubscribe;

  /**
   * Emit an event to all subscribers.
   */
  emit<T = unknown>(event: string, payload?: T): void;

  /**
   * Remove all listeners for an event.
   */
  off(event: string): void;

  /**
   * Remove all listeners for all events.
   */
  clear(): void;
}

/**
 * Event bus options.
 */
export interface EventBusOptions {
  /** Enable debug logging */
  debug?: boolean;
  /** Maximum listeners per event (default: 100) */
  maxListeners?: number;
}

// EventBusRuntime removed - use setEventBusAdapter/useEventBus/resetEventBusAdapter instead

// ============================================
// Event Bus Implementation
// ============================================

/**
 * Create event bus implementation.
 */
export function createEventBus(options: EventBusOptions = {}): EventBus {
  const { debug = false, maxListeners = 100 } = options;
  const listeners = new Map<string, Set<EventHandler>>(); // Use Set to prevent duplicates

  const log = (...args: unknown[]) => {
    if (debug) {
      useLogger().debug('[EventBus]', ...args);
    }
  };

  return {
    on<T = unknown>(event: string, handler: EventHandler<T>): Unsubscribe {
      let handlers = listeners.get(event);

      if (!handlers) {
        handlers = new Set<EventHandler>();
        listeners.set(event, handlers);
      }

      // Check max listeners
      if (handlers.size >= maxListeners) {
        useLogger().warn(`[EventBus] Max listeners (${maxListeners}) reached for event: ${event}`);
      }

      handlers.add(handler as EventHandler);
      log('subscribed', { event, handlerCount: handlers.size });

      // Return unsubscribe function. Re-read the Set from the map instead of
      // closing over the one captured at subscribe time: `off(event)` drops the
      // whole key, so a later `on(event, ...)` installs a FRESH Set. A stale
      // closure would then delete its own orphaned handler, observe size 0, and
      // wipe the live key - silently unsubscribing everyone who subscribed after
      // the off(). Matches `once()` below.
      return () => {
        const currentHandlers = listeners.get(event);
        currentHandlers?.delete(handler as EventHandler);
        if (currentHandlers?.size === 0) {
          listeners.delete(event);
        }
        log('unsubscribed', { event, handlerCount: currentHandlers?.size ?? 0 });
      };
    },

    once<T = unknown>(event: string, handler: EventHandler<T>): Unsubscribe {
      const wrappedHandler: EventHandler = (payload: unknown) => {
        // 先移除自身，再调用 handler
        const currentHandlers = listeners.get(event);
        currentHandlers?.delete(wrappedHandler);
        if (currentHandlers?.size === 0) {
          listeners.delete(event);
        }
        handler(payload as T);
      };

      let handlers = listeners.get(event);
      if (!handlers) {
        handlers = new Set<EventHandler>();
        listeners.set(event, handlers);
      }
      handlers.add(wrappedHandler);

      log('subscribed once', { event, handlerCount: handlers.size });

      // Return unsubscribe function
      return () => {
        const currentHandlers = listeners.get(event);
        currentHandlers?.delete(wrappedHandler);
        if (currentHandlers?.size === 0) {
          listeners.delete(event);
        }
        log('unsubscribed once', { event });
      };
    },

    emit<T = unknown>(event: string, payload?: T): void {
      const handlers = listeners.get(event);

      if (!handlers) {
        log('no listeners', { event });
        return;
      }

      log('emitting', { event, payload, handlerCount: handlers.size });

      // Call all handlers
      for (const handler of handlers) {
        try {
          handler(payload as T);
        } catch (error) {
          useLogger().error(`[EventBus] Error in handler for event "${event}":`, error);
        }
      }
    },

    off(event: string): void {
      listeners.delete(event);
      log('cleared all listeners', { event });
    },

    clear(): void {
      listeners.clear();
      log('cleared all events');
    },
  };
}

// ============================================
// Singleton
// ============================================

// The fallback bus is shared through the registry too: a per-chunk copy would
// route publishers and subscribers that resolved different entry points into
// separate buses and drop every event between them.
const _slot = createAdapterSingleton<EventBus>('event-bus', () => createEventBus());

export function setEventBusAdapter(eventBus: EventBus): void {
  _slot.set(eventBus);
}

export function useEventBus(): EventBus {
  return _slot.use();
}

export function resetEventBusAdapter(): void {
  _slot.reset();
}
