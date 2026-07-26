/**
 * @tnzi/core/adapters/logger
 *
 * Logger adapter for consistent logging across core.
 */

import { createAdapterSingleton } from './singleton';

export interface LoggerAdapter {
  debug(message: string, ...args: unknown[]): void;
  info(message: string, ...args: unknown[]): void;
  warn(message: string, ...args: unknown[]): void;
  error(message: string, ...args: unknown[]): void;
}

class ConsoleLoggerAdapter implements LoggerAdapter {
  debug(message: string, ...args: unknown[]): void { console.debug(message, ...args); }
  info(message: string, ...args: unknown[]): void { console.log(message, ...args); }
  warn(message: string, ...args: unknown[]): void { console.warn(message, ...args); }
  error(message: string, ...args: unknown[]): void { console.error(message, ...args); }
}

const _slot = createAdapterSingleton<LoggerAdapter>('logger', () => new ConsoleLoggerAdapter());

export function setLoggerAdapter(adapter: LoggerAdapter): void {
  _slot.set(adapter);
}

export function useLogger(): LoggerAdapter {
  return _slot.use();
}

export function resetLoggerAdapter(): void {
  _slot.reset();
}
