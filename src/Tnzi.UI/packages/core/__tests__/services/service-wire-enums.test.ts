import { describe, it, expect } from 'vitest';
import * as Presence from '../../src/services/presence';
import {
  TwoFactorType,
  LoginStatus,
  PasswordStrengthLevel,
  AbnormalLoginType,
  AbnormalLoginAction,
  Gender,
} from '../../src/services/identity/metadata';
import { HealthStatus } from '../../src/services/system/metadata';

/**
 * This file used to also assert an aggregate `src/services/index.ts` barrel
 * that re-exported all 19 modules as namespaces (`export * as Identity`).
 *
 * That barrel was removed on 2026-08-15 along with `export * from
 * "./services/index"` in the package root: it had zero consumers across the
 * whole ecosystem, yet it made `import { formatDateTime } from '@tnzi/core'`
 * cost 336,714 B and all 107 admin endpoint literals. Services are reached by
 * area instead - `@tnzi/core/services/identity` and friends.
 *
 * The coverage it provided ("a new service directory must not be forgotten")
 * moved to `__tests__/conventions/dist-treeshakeable.test.ts`, which walks
 * `src/services/*` and asserts each one has a built subpath. That is the
 * stronger check of the two: a namespace on a barrel proved the name existed,
 * whereas a built subpath proves consumers can actually import it.
 */
describe('presence wire contract', () => {
  it('is reachable by area and mirrors the backend member names', () => {
    expect(Presence.UserPresenceStatus.Online).toBe('Online');
    expect(typeof Presence.usePresenceApi).toBe('function');
  });
});

/**
 * Response-side enums are serialized by the backend's global
 * JsonStringEnumConverter, so the TS mirrors must be STRING enums (member name
 * = value) or a `dto.field === Enum.Member` comparison silently never matches.
 */
describe('identity wire enums', () => {
  it('TwoFactorType mirrors the backend member names', () => {
    expect(TwoFactorType.Sms).toBe('Sms');
    expect(TwoFactorType.Email).toBe('Email');
    expect(TwoFactorType.Totp).toBe('Totp');
  });

  it('LoginStatus mirrors the backend member names', () => {
    expect(LoginStatus.Success).toBe('Success');
    expect(LoginStatus.Failed).toBe('Failed');
  });

  it('PasswordStrengthLevel mirrors the backend member names', () => {
    expect(PasswordStrengthLevel.VeryWeak).toBe('VeryWeak');
    expect(PasswordStrengthLevel.Fair).toBe('Fair');
    expect(PasswordStrengthLevel.VeryStrong).toBe('VeryStrong');
  });

  it('AbnormalLoginType covers every backend member', () => {
    expect(Object.values(AbnormalLoginType)).toEqual([
      'NewDevice',
      'NewIpAddress',
      'LocationChange',
      'ImpossibleTravel',
      'FrequentAttempts',
      'UnusualTime',
    ]);
  });

  it('AbnormalLoginAction covers every backend member, including Block', () => {
    expect(Object.values(AbnormalLoginAction)).toEqual([
      'None',
      'Notify',
      'RequireVerification',
      'Block',
    ]);
  });

  it('matches a raw 2FA challenge payload without coercion', () => {
    const wire = JSON.parse('{"supportedTypes":["Sms","Totp"]}');
    expect(wire.supportedTypes).toContain(TwoFactorType.Sms);
    expect(wire.supportedTypes).toContain(TwoFactorType.Totp);
    expect(wire.supportedTypes).not.toContain(TwoFactorType.Email);
  });

  it('Gender stays numeric: the backend DTO field is an int, not an enum', () => {
    expect(Gender.Male).toBe(1);
    expect(Gender.Female).toBe(2);
  });
});

describe('system wire enums', () => {
  it('HealthStatus mirrors the health payload strings', () => {
    // Tnzi.HealthChecks writes `report.Status.ToString()`.
    expect(HealthStatus.Healthy).toBe('Healthy');
    expect(HealthStatus.Degraded).toBe('Degraded');
    expect(HealthStatus.Unhealthy).toBe('Unhealthy');
  });
});
