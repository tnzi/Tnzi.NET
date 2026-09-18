import { describe, it, expect, vi } from 'vitest';
import { useCouponApi } from '../../src/services/payment/api';
import type {
  CouponValidationResponseDto,
  DiscountCalculationResultDto,
} from '../../src/services/payment/types';

/**
 * `useCouponApi` used to carry two legacy methods, `apply` and `validate`, typed
 * as a `CouponValidationDto` shape that neither endpoint answers: calculate-discount
 * returns `DiscountCalculationResultDto` (no `isValid`), validate-coupon returns
 * the narrowed `CouponValidationResponseDto` (`coupon`, not `promotion`). A
 * consumer branching on `res.data.isValid` after `apply()` therefore treated every
 * valid coupon as invalid, and TypeScript accepted every line. The typed
 * siblings are the only way to call these endpoints now.
 */

function mockClient() {
  return {
    post: vi.fn(async (_url: string, body: unknown) => ({
      succeeded: true,
      success: true,
      code: 200,
      data: body,
    })),
    get: vi.fn(),
  };
}

describe('useCouponApi discount endpoints', () => {
  it('does not expose the legacy apply/validate methods with the wrong response shape', () => {
    const api = useCouponApi(mockClient() as never) as Record<string, unknown>;
    expect(api).not.toHaveProperty('apply');
    expect(api).not.toHaveProperty('validate');
  });

  it('calculateDiscount posts the request verbatim and is typed as the discount result', async () => {
    const c = mockClient();
    const result = await useCouponApi(c as never).calculateDiscount({
      couponCode: 'SAVE10',
      orderAmount: 100,
    });
    expect(c.post).toHaveBeenCalledWith('/promotions/calculate-discount', {
      couponCode: 'SAVE10',
      orderAmount: 100,
    });
    const typed: DiscountCalculationResultDto | undefined = result.data;
    expect(typed).toBeDefined();
  });

  it('validateCoupon posts the request verbatim and is typed as the validation response', async () => {
    const c = mockClient();
    const result = await useCouponApi(c as never).validateCoupon({
      couponCode: 'SAVE10',
      orderAmount: 100,
    });
    expect(c.post).toHaveBeenCalledWith('/promotions/validate-coupon', {
      couponCode: 'SAVE10',
      orderAmount: 100,
    });
    const typed: CouponValidationResponseDto | undefined = result.data;
    expect(typed).toBeDefined();
  });
});
