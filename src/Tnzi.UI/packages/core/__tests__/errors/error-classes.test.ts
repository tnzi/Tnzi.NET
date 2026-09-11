import { describe, it, expect } from 'vitest';
import { BusinessError } from '../../src/errors/business-error';
import { ValidationError } from '../../src/errors/validation-error';
import {
  NetworkError,
  TimeoutError,
  AbortError,
  isNetworkError,
  isTimeoutError,
  isAbortError,
} from '../../src/errors/network-error';

describe('BusinessError', () => {
  it('carries the code and status alongside the message', () => {
    const error = new BusinessError('Order already shipped', 'OrderShipped', 409);

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe('BusinessError');
    expect(error.message).toBe('Order already shipped');
    expect(error.code).toBe('OrderShipped');
    expect(error.statusCode).toBe(409);
  });
});

describe('ValidationError', () => {
  it('renders the field and every per-field message in toString', () => {
    const error = new ValidationError('Invalid input', 'email', {
      email: ['is required', 'must be an address'],
      age: ['must be positive'],
    });

    expect(error.name).toBe('ValidationError');
    expect(error.toString()).toBe(
      [
        'ValidationError: Invalid input',
        '  Field: email',
        '  email: is required',
        '  email: must be an address',
        '  age: must be positive',
      ].join('\n')
    );
  });

  it('renders just the headline when there is no field detail', () => {
    expect(new ValidationError('Invalid input').toString()).toBe('ValidationError: Invalid input');
  });
});

describe('network errors', () => {
  it('NetworkError keeps the underlying cause', () => {
    const cause = new Error('ECONNREFUSED');
    const error = new NetworkError('Connection failed', { cause });

    expect(error.name).toBe('NetworkError');
    expect(error.cause).toBe(cause);
    expect(new NetworkError('no cause').cause).toBeUndefined();
  });

  it('TimeoutError names the timeout when it is known', () => {
    expect(new TimeoutError(3000).message).toBe('Request timed out after 3000ms');
    expect(new TimeoutError().message).toBe('Request timed out');
    expect(new TimeoutError().name).toBe('TimeoutError');
  });

  it('AbortError has a fixed message and name', () => {
    const error = new AbortError();

    expect(error.message).toBe('Request was aborted');
    expect(error.name).toBe('AbortError');
  });

  it('type guards distinguish the hierarchy and reject foreign errors', () => {
    const timeout = new TimeoutError(10);
    const abort = new AbortError();
    const plain = new Error('x');

    expect(isNetworkError(timeout)).toBe(true);
    expect(isNetworkError(abort)).toBe(true);
    expect(isNetworkError(plain)).toBe(false);
    expect(isNetworkError('not an error')).toBe(false);

    expect(isTimeoutError(timeout)).toBe(true);
    expect(isTimeoutError(abort)).toBe(false);

    expect(isAbortError(abort)).toBe(true);
    expect(isAbortError(timeout)).toBe(false);
  });
});
