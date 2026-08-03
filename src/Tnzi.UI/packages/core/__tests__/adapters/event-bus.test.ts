import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createEventBus } from '../../src/adapters/event-bus/index';
import type { EventBus } from '../../src/adapters/event-bus/index';

describe('EventBus', () => {
  let bus: EventBus;

  beforeEach(() => {
    bus = createEventBus();
  });

  // ------------------------------------------
  // on / emit
  // ------------------------------------------

  describe('on + emit', () => {
    it('should call handler when event is emitted', () => {
      const handler = vi.fn();
      bus.on('test', handler);
      bus.emit('test', 'payload');
      expect(handler).toHaveBeenCalledWith('payload');
    });

    it('should support multiple handlers for same event', () => {
      const h1 = vi.fn();
      const h2 = vi.fn();
      bus.on('test', h1);
      bus.on('test', h2);
      bus.emit('test', 42);
      expect(h1).toHaveBeenCalledWith(42);
      expect(h2).toHaveBeenCalledWith(42);
    });

    it('should not call handlers for different events', () => {
      const handler = vi.fn();
      bus.on('eventA', handler);
      bus.emit('eventB', 'data');
      expect(handler).not.toHaveBeenCalled();
    });

    it('should handle emit with no payload', () => {
      const handler = vi.fn();
      bus.on('test', handler);
      bus.emit('test');
      expect(handler).toHaveBeenCalledWith(undefined);
    });

    it('should not call handler after unsubscribe', () => {
      const handler = vi.fn();
      const unsub = bus.on('test', handler);
      unsub();
      bus.emit('test', 'data');
      expect(handler).not.toHaveBeenCalled();
    });

    it('should deduplicate same handler reference', () => {
      const handler = vi.fn();
      bus.on('test', handler);
      bus.on('test', handler); // Same reference, Set deduplicates
      bus.emit('test', 'data');
      expect(handler).toHaveBeenCalledTimes(1);
    });
  });

  // ------------------------------------------
  // once
  // ------------------------------------------

  describe('once', () => {
    it('should call handler only once', () => {
      const handler = vi.fn();
      bus.once('test', handler);
      bus.emit('test', 'first');
      bus.emit('test', 'second');
      expect(handler).toHaveBeenCalledTimes(1);
      expect(handler).toHaveBeenCalledWith('first');
    });

    it('should be removable before first emit via unsubscribe', () => {
      const handler = vi.fn();
      const unsub = bus.once('test', handler);
      unsub();
      bus.emit('test', 'data');
      expect(handler).not.toHaveBeenCalled();
    });
  });

  // ------------------------------------------
  // off
  // ------------------------------------------

  describe('off', () => {
    it('should remove all handlers for an event', () => {
      const h1 = vi.fn();
      const h2 = vi.fn();
      bus.on('test', h1);
      bus.on('test', h2);
      bus.off('test');
      bus.emit('test', 'data');
      expect(h1).not.toHaveBeenCalled();
      expect(h2).not.toHaveBeenCalled();
    });

    it('should not affect handlers for other events', () => {
      const handler = vi.fn();
      bus.on('keep', handler);
      bus.off('other');
      bus.emit('keep', 'data');
      expect(handler).toHaveBeenCalledWith('data');
    });

    it('a stale unsubscribe from before off() must not wipe later subscribers', () => {
      // on() -> SetA; off() orphans SetA; on() installs a fresh SetB. Calling
      // the first unsubscribe used to delete its handler from the orphaned
      // SetA, see size 0, and drop the whole event key - silently taking SetB
      // (and everyone in it) with it.
      const first = vi.fn();
      const second = vi.fn();

      const offFirst = bus.on('test', first);
      bus.off('test');
      bus.on('test', second);

      offFirst();
      bus.emit('test', 'data');

      expect(second).toHaveBeenCalledWith('data');
      expect(first).not.toHaveBeenCalled();
    });

    it('unsubscribing one handler leaves its siblings subscribed', () => {
      const h1 = vi.fn();
      const h2 = vi.fn();
      const off1 = bus.on('test', h1);
      bus.on('test', h2);

      off1();
      bus.emit('test', 'data');

      expect(h1).not.toHaveBeenCalled();
      expect(h2).toHaveBeenCalledWith('data');
    });

    it('calling an unsubscribe twice does not throw or disturb others', () => {
      const gone = vi.fn();
      const kept = vi.fn();
      const off = bus.on('test', gone);
      bus.on('test', kept);

      off();
      expect(() => off()).not.toThrow();
      bus.emit('test', 'data');

      expect(gone).not.toHaveBeenCalled();
      expect(kept).toHaveBeenCalledWith('data');
    });
  });

  // ------------------------------------------
  // clear
  // ------------------------------------------

  describe('clear', () => {
    it('should remove all handlers for all events', () => {
      const h1 = vi.fn();
      const h2 = vi.fn();
      bus.on('eventA', h1);
      bus.on('eventB', h2);
      bus.clear();
      bus.emit('eventA', 'data');
      bus.emit('eventB', 'data');
      expect(h1).not.toHaveBeenCalled();
      expect(h2).not.toHaveBeenCalled();
    });
  });

  // ------------------------------------------
  // Error isolation
  // ------------------------------------------

  describe('error isolation', () => {
    it('should continue calling other handlers if one throws', () => {
      const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
      const throwingHandler = () => { throw new Error('boom'); };
      const goodHandler = vi.fn();

      bus.on('test', throwingHandler);
      bus.on('test', goodHandler);
      bus.emit('test', 'data');

      expect(goodHandler).toHaveBeenCalledWith('data');
      expect(errorSpy).toHaveBeenCalled();
      errorSpy.mockRestore();
    });
  });

  // ------------------------------------------
  // Max listeners warning
  // ------------------------------------------

  describe('maxListeners', () => {
    it('should warn when max listeners reached', () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
      const smallBus = createEventBus({ maxListeners: 2 });

      smallBus.on('test', () => {});
      smallBus.on('test', () => {});
      smallBus.on('test', () => {}); // Triggers warning

      expect(warnSpy).toHaveBeenCalled();
      warnSpy.mockRestore();
    });
  });

  // ------------------------------------------
  // emit with no subscribers
  // ------------------------------------------

  describe('emit with no subscribers', () => {
    it('should not throw when emitting to no listeners', () => {
      expect(() => bus.emit('nonexistent', 'data')).not.toThrow();
    });
  });

  // ------------------------------------------
  // Cleanup after unsubscribe
  // ------------------------------------------

  describe('cleanup', () => {
    it('should remove event key from map when last handler is removed', () => {
      const handler = vi.fn();
      const unsub = bus.on('test', handler);
      unsub();
      // Emitting should not throw (event key cleaned up)
      bus.emit('test', 'data');
      expect(handler).not.toHaveBeenCalled();
    });
  });

  // ------------------------------------------
  // Debug mode
  // ------------------------------------------

  describe('debug mode', () => {
    it('should log events when debug is true', () => {
      const debugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {});
      const debugBus = createEventBus({ debug: true });
      debugBus.on('test', () => {});
      debugBus.emit('test', 'data');

      expect(debugSpy).toHaveBeenCalled();
      debugSpy.mockRestore();
    });
  });
});
