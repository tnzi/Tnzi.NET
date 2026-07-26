import { describe, it, expect } from 'vitest';
import * as Services from '../../services/index';
import {
  TwoFactorType,
  LoginStatus,
  PasswordStrengthLevel,
  AbnormalLoginType,
  AbnormalLoginAction,
  Gender,
} from '../../services/identity/metadata';
import { HealthStatus } from '../../services/system/metadata';

/**
 * Every `src/services/*` directory must appear as a namespace on the barrel.
 * `presence` was missing, so `import { Presence } from '@tnzi/core'` failed
 * even though the subpath export and the tsup entry both existed.
 */
const EXPECTED_NAMESPACES = [
  'AI',
  'Authorization',
  'Identity',
  'Payment',
  'Finance',
  'Payroll',
  'Chat',
  'Presence',
  'Notification',
  'Storage',
  'System',
  'Audit',
  'Template',
  'Logging',
  'Diagnostics',
  'Performance',
  'SignalR',
  'Localization',
] as const;

describe('services barrel', () => {
  it.each(EXPECTED_NAMESPACES)('exports the %s namespace', (name) => {
    expect(Services[name as keyof typeof Services]).toBeDefined();
  });

  it('exports exactly the expected namespaces', () => {
    expect(Object.keys(Services).sort()).toEqual([...EXPECTED_NAMESPACES].sort());
  });

  it('exposes the presence contract through the namespace', () => {
    expect(Services.Presence.UserPresenceStatus.Online).toBe('Online');
    expect(typeof Services.Presence.usePresenceApi).toBe('function');
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
