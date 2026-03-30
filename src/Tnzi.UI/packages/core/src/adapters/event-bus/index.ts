/**
 * @tnzi/core/adapters/event-bus
 *
 * Event bus for cross-module communication.
 */

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

/**
 * Event bus runtime for managing active event bus.
 */
export interface EventBusRuntime {
  eventBus: EventBus;
  use(): EventBus;
}

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
      console.debug('[EventBus]', ...args);
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
        console.warn(`[EventBus] Max listeners (${maxListeners}) reached for event: ${event}`); // TODO: Replace with logger injection
      }

      handlers.add(handler as EventHandler);
      log('subscribed', { event, handlerCount: handlers.size });

      // Return unsubscribe function
      return () => {
        handlers?.delete(handler as EventHandler);
        if (handlers?.size === 0) {
          listeners.delete(event);
        }
        log('unsubscribed', { event, handlerCount: handlers?.size ?? 0 });
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
          console.error(`[EventBus] Error in handler for event "${event}":`, error); // TODO: Replace with logger injection
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
// Event Bus Runtime
// ============================================

/**
 * Create event bus runtime.
 */
export function createEventBusRuntime(options: EventBusOptions = {}): EventBusRuntime {
  const eventBus = createEventBus(options);

  return {
    eventBus,
    use(): EventBus {
      return eventBus;
    },
  };
}

// ============================================
// Default Runtime Instance
// ============================================

const defaultEventBusRuntime = createEventBusRuntime();
let activeEventBusRuntime: EventBusRuntime = defaultEventBusRuntime;

/**
 * Set active event bus runtime.
 */
export function setActiveEventBusRuntime(runtime: EventBusRuntime): void {
  activeEventBusRuntime = runtime;
}

/**
 * Get active event bus runtime.
 */
export function getActiveEventBusRuntime(): EventBusRuntime {
  return activeEventBusRuntime;
}

/**
 * Get active event bus (convenience function).
 */
export function useEventBus(): EventBus {
  return activeEventBusRuntime.use();
}

/**
 * Reset event bus runtime to default. For tests and SSR isolation.
 */
export function resetEventBusRuntime(): void {
  activeEventBusRuntime = defaultEventBusRuntime;
}
