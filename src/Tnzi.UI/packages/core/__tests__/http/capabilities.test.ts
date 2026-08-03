import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { HttpClient } from '../../src/http/http';

import {
  CAPABILITY_HEADER,
  buildCapabilityHeaderValue,
  declareClientCapability,
  fetchServerCapabilities,
  getClientCapabilities,
  isValidCapabilityName,
  resetClientCapabilities,
  serverSupports,
} from '../../src/http/capabilities';
import type { CapabilityHttpClient } from '../../src/http/capabilities';

describe('capability negotiation (client)', () => {
  beforeEach(() => {
    resetClientCapabilities();
  });

  it('accepts kebab-case names with a version suffix', () => {
    expect(isValidCapabilityName('chat-draft-restore-v1')).toBe(true);
    expect(isValidCapabilityName('rpc-v1')).toBe(true);
    expect(isValidCapabilityName('streaming-frames-v12')).toBe(true);
  });

  it('rejects names that could silently drift apart from the server', () => {
    // Each of these would be a *different* capability than the one intended, and the symptom is
    // an absence: negotiation never matches and the newer path is simply never taken.
    expect(isValidCapabilityName('no-version-suffix')).toBe(false);
    expect(isValidCapabilityName('Chat-Draft-Restore-v1')).toBe(false);
    expect(isValidCapabilityName('chat_draft_restore_v1')).toBe(false);
    expect(isValidCapabilityName('chat-draft-restore-v0')).toBe(false);
  });

  it('throws on declaring a malformed name', () => {
    expect(() => declareClientCapability('NotValid')).toThrow(/not a valid capability name/);
  });

  it('declares idempotently and reports sorted', () => {
    declareClientCapability('zebra-v1');
    declareClientCapability('alpha-v1');
    declareClientCapability('zebra-v1');

    expect(getClientCapabilities()).toEqual(['alpha-v1', 'zebra-v1']);
  });

  it('omits the header entirely when nothing is declared', () => {
    // The default state for every deployment today - an empty header on every request would be
    // pure noise.
    expect(buildCapabilityHeaderValue()).toBeUndefined();
  });

  it('builds a comma-separated header value', () => {
    declareClientCapability('alpha-v1');
    declareClientCapability('beta-v1');

    expect(buildCapabilityHeaderValue()).toBe('alpha-v1,beta-v1');
    expect(CAPABILITY_HEADER).toBe('X-Tnzi-Capabilities');
  });

  it('does not treat the server list as its own declaration', () => {
    // The failure the mechanism exists to prevent, mirrored on the client: the server advertising
    // something says nothing about whether this build knows how to speak it.
    const fromServer = ['alpha-v1'];

    expect(serverSupports(fromServer, 'alpha-v1')).toBe(true);
    expect(getClientCapabilities()).toEqual([]);
  });

  it('returns an empty list when the capability endpoint fails', async () => {
    const failing: CapabilityHttpClient = { get: async () => ({ succeeded: false }) };
    const throwing: CapabilityHttpClient = {
      get: async () => {
        throw new Error('network down');
      },
    };

    // Unknown must degrade to "assume nothing new", never to "assume everything works".
    await expect(fetchServerCapabilities(failing)).resolves.toEqual([]);
    await expect(fetchServerCapabilities(throwing)).resolves.toEqual([]);
  });

  it('reads the capability list from a successful response', async () => {
    const client: CapabilityHttpClient = {
      get: async <T,>() => ({
        succeeded: true,
        data: { capabilities: ['alpha-v1'] } as T,
      }),
    };

    await expect(fetchServerCapabilities(client)).resolves.toEqual(['alpha-v1']);
  });

  describe('request wiring', () => {
    afterEach(() => {
      vi.unstubAllGlobals();
    });

    /** Headers of the first fetch call, or `{}` if fetch was never reached. */
    function sentHeaders(fetchMock: { mock: { calls: unknown[][] } }): Record<string, string> {
      const init = fetchMock.mock.calls[0]?.[1] as { headers?: Record<string, string> } | undefined;
      return init?.headers ?? {};
    }

    it('sends the declaration on ordinary requests', async () => {
      declareClientCapability('alpha-v1');
      const fetchMock = vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ succeeded: true, code: 200 }), { status: 200 })
      );
      vi.stubGlobal('fetch', fetchMock);

      await new HttpClient({ baseUrl: '/api' }).get('/thing');

      expect(sentHeaders(fetchMock)[CAPABILITY_HEADER]).toBe('alpha-v1');
    });

    it('sends the declaration on uploads too', async () => {
      declareClientCapability('alpha-v1');
      const headers: Record<string, string> = {};

      // Uploads go through XHR rather than buildHeaders. Without an explicit line there they
      // would be the one request kind that silently negotiates as an old client.
      class FakeXhr {
        status = 200;
        responseText = JSON.stringify({ succeeded: true, code: 200 });
        timeout = 0;
        withCredentials = false;
        upload = { onprogress: null as unknown };
        onload: (() => void) | null = null;
        onerror: (() => void) | null = null;
        ontimeout: (() => void) | null = null;
        open() {}
        setRequestHeader(key: string, value: string) {
          headers[key] = value;
        }
        send() {
          queueMicrotask(() => this.onload?.());
        }
      }
      vi.stubGlobal('XMLHttpRequest', FakeXhr);

      await new HttpClient({ baseUrl: '/api' }).uploadFormData('/files/upload', new FormData());

      expect(headers[CAPABILITY_HEADER]).toBe('alpha-v1');
    });

    it('sends no capability header when nothing is declared', async () => {
      const fetchMock = vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ succeeded: true, code: 200 }), { status: 200 })
      );
      vi.stubGlobal('fetch', fetchMock);

      await new HttpClient({ baseUrl: '/api' }).get('/thing');

      expect(sentHeaders(fetchMock)[CAPABILITY_HEADER]).toBeUndefined();
    });
  });
});
