/**
 * @tnzi/core/adapters/logger
 *
 * Logger adapter for consistent logging across core.
 */

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

const _fallback = new ConsoleLoggerAdapter();
let _active: LoggerAdapter | null = null;

export function setLoggerAdapter(adapter: LoggerAdapter): void {
  _active = adapter;
}

export function useLogger(): LoggerAdapter {
  return _active ?? _fallback;
}

export function resetLoggerAdapter(): void {
  _active = null;
}
