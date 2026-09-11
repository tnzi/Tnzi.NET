import { describe, it, expect } from 'vitest';
import { parseDeviceInfo } from '../../src/utils/device-parser';

const UNKNOWN = { icon: 'mdi:devices', label: '-', osFamily: 'unknown' };

describe('parseDeviceInfo', () => {
  it('returns the stable unknown profile for empty or non-string input', () => {
    expect(parseDeviceInfo(null)).toEqual(UNKNOWN);
    expect(parseDeviceInfo(undefined)).toEqual(UNKNOWN);
    expect(parseDeviceInfo('')).toEqual(UNKNOWN);
    expect(parseDeviceInfo(42 as unknown as string)).toEqual(UNKNOWN);
  });

  it('returns the unknown profile when no OS family matches', () => {
    expect(parseDeviceInfo('Nintendo Switch; WebKit')).toEqual(UNKNOWN);
  });

  it('reads the pre-formatted deviceInfo the backend records at login', () => {
    expect(parseDeviceInfo('Windows 10 / Chrome 120')).toEqual({
      icon: 'mdi:microsoft-windows',
      label: 'Windows · Chrome',
      osFamily: 'windows',
    });
  });

  it('parses a raw desktop user agent', () => {
    const ua =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';

    expect(parseDeviceInfo(ua)).toEqual({
      icon: 'mdi:microsoft-windows',
      label: 'Windows · Chrome',
      osFamily: 'windows',
    });
  });

  it('reports Edge and Opera rather than the Chrome token they also carry', () => {
    const edge =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0';
    const opera =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0';

    expect(parseDeviceInfo(edge).label).toBe('Windows · Edge');
    expect(parseDeviceInfo(opera).label).toBe('Windows · Opera');
  });

  it('classifies an iPhone as iOS even though the UA also says "like Mac OS X"', () => {
    const ua =
      'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1';

    expect(parseDeviceInfo(ua)).toEqual({ icon: 'mdi:apple-ios', label: 'iOS · Safari', osFamily: 'ios' });
  });

  it('classifies Android as Android even though the UA also says Linux', () => {
    const ua =
      'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36';

    expect(parseDeviceInfo(ua)).toEqual({ icon: 'mdi:android', label: 'Android · Chrome', osFamily: 'android' });
  });

  it('parses macOS and Linux desktops', () => {
    const mac =
      'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15';
    const linux = 'Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:120.0) Gecko/20100101 Firefox/120.0';

    expect(parseDeviceInfo(mac)).toEqual({ icon: 'mdi:apple', label: 'macOS · Safari', osFamily: 'mac' });
    expect(parseDeviceInfo(linux)).toEqual({ icon: 'mdi:linux', label: 'Linux · Firefox', osFamily: 'linux' });
  });

  it('falls back to the OS label alone when no browser is recognised', () => {
    expect(parseDeviceInfo('macOS').label).toBe('macOS');
  });
});
