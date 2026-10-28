import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useSubscriptionApi } from '../../src/services/payment/api';
import {
  isSubscriptionSuspendedError,
  subscriptionSuspendedDetailsOf,
  SUBSCRIPTION_SUSPENDED,
} from '../../src/services/payment/standing';
import {
  loadStripeClient,
  resetStripeClientCache,
  isStripeJsMissingError,
  setupIntentPaymentMethodId,
} from '../../src/services/payment/stripe';
import { HttpError } from '../../src/errors/api-error';

const loadStripe = vi.fn();
vi.mock('@stripe/stripe-js', () => ({ loadStripe: (key: string) => loadStripe(key) }));

describe('useSubscriptionApi().getStanding', () => {
  it('reads subscriptions/standing, with the product only when given', async () => {
    const client = { get: vi.fn(async () => ({ succeeded: true, code: 200, data: {} })) };
    const api = useSubscriptionApi(client as never);

    await api.getStanding();
    await api.getStanding('crm');

    expect(client.get).toHaveBeenNthCalledWith(1, '/subscriptions/standing', { params: undefined });
    expect(client.get).toHaveBeenNthCalledWith(2, '/subscriptions/standing', { params: { productCode: 'crm' } });
  });
});

describe('isSubscriptionSuspendedError', () => {
  const envelope = {
    succeeded: false,
    code: 402,
    message: 'Service suspended',
    errorCode: SUBSCRIPTION_SUSPENDED,
    errorDetails: { standing: 'Suspended', canManage: true },
  };

  it('recognises the failed envelope', () => {
    expect(isSubscriptionSuspendedError(envelope)).toBe(true);
    expect(subscriptionSuspendedDetailsOf(envelope)).toEqual({ standing: 'Suspended', canManage: true });
  });

  it('recognises the HttpError thrown from it', () => {
    const error = new HttpError(envelope as never);
    expect(isSubscriptionSuspendedError(error)).toBe(true);
    expect(subscriptionSuspendedDetailsOf(error)?.canManage).toBe(true);
  });

  it('recognises a hub invocation error naming the code', () => {
    const error = new Error("An unexpected error occurred invoking 'Send' on the server. HubException: SUBSCRIPTION_SUSPENDED");
    expect(isSubscriptionSuspendedError(error)).toBe(true);
  });

  it('ignores other 402s, other errors and successes', () => {
    expect(isSubscriptionSuspendedError({ succeeded: false, code: 402, errorCode: 'CARD_DECLINED' })).toBe(false);
    expect(isSubscriptionSuspendedError({ succeeded: true, code: 200, errorCode: SUBSCRIPTION_SUSPENDED })).toBe(false);
    expect(isSubscriptionSuspendedError({ message: SUBSCRIPTION_SUSPENDED })).toBe(false);
    expect(isSubscriptionSuspendedError(null)).toBe(false);
    expect(isSubscriptionSuspendedError('SUBSCRIPTION_SUSPENDED')).toBe(false);
  });
});

describe('loadStripeClient', () => {
  beforeEach(() => {
    resetStripeClientCache();
    loadStripe.mockReset();
  });

  it('loads Stripe.js once per publishable key', async () => {
    const stripe = { elements: vi.fn() };
    loadStripe.mockResolvedValue(stripe);

    const [a, b] = await Promise.all([loadStripeClient('pk_test_1'), loadStripeClient('pk_test_1')]);

    expect(a).toBe(stripe);
    expect(b).toBe(stripe);
    expect(loadStripe).toHaveBeenCalledTimes(1);
  });

  it('rejects when Stripe.js could not load, and retries next time', async () => {
    loadStripe.mockResolvedValueOnce(null).mockResolvedValueOnce({ elements: vi.fn() });

    await expect(loadStripeClient('pk_test_2')).rejects.toThrow('Stripe.js could not be loaded');
    await expect(loadStripeClient('pk_test_2')).resolves.toBeTruthy();
    expect(loadStripe).toHaveBeenCalledTimes(2);
  });

  it('rejects without a key', async () => {
    await expect(loadStripeClient('')).rejects.toThrow('publishable key');
  });

  it('names the missing optional peer', () => {
    const error = Object.assign(new Error('Card collection needs the @stripe/stripe-js package'), { name: 'StripeJsMissingError' });
    expect(isStripeJsMissingError(error)).toBe(true);
    expect(isStripeJsMissingError(new Error('x'))).toBe(false);
  });
});

describe('setupIntentPaymentMethodId', () => {
  it('reads the id in both shapes Stripe returns', () => {
    expect(setupIntentPaymentMethodId({ payment_method: 'pm_1' })).toBe('pm_1');
    expect(setupIntentPaymentMethodId({ payment_method: { id: 'pm_2' } })).toBe('pm_2');
    expect(setupIntentPaymentMethodId({ payment_method: null })).toBeUndefined();
    expect(setupIntentPaymentMethodId(undefined)).toBeUndefined();
  });
});
