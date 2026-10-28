import { describe, it, expect, vi } from 'vitest';
import { loadStripeClient, isStripeJsMissingError } from '../../src/services/payment/stripe';

// The optional peer is absent: importing it fails the way a bundler's missing
// module does.
vi.mock('@stripe/stripe-js', () => {
  throw new Error("Cannot find module '@stripe/stripe-js'");
});

describe('loadStripeClient without @stripe/stripe-js', () => {
  it('rejects with a StripeJsMissingError that says what to install', async () => {
    const error = await loadStripeClient('pk_test_missing').catch((e: unknown) => e);
    expect(isStripeJsMissingError(error)).toBe(true);
    expect((error as Error).message).toBe('Card collection needs the @stripe/stripe-js package');
  });
});
