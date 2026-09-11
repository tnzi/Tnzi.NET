import { describe, it, expect, vi } from 'vitest';
import { useInvoiceApi } from '../../src/services/payment/api';

/**
 * `GET invoices/{id}/pdf` streams the invoice file. It used to answer a string
 * that was its own path - a link pointing at itself - so a client following it
 * only ever got the same string back. The api must therefore read it as a
 * download, and the shareable address lives on its own endpoint.
 */

function mockClient() {
  return {
    get: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: 'https://files.example/inv.pdf' })),
    download: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: new Blob(['%PDF']) })),
  };
}

describe('useInvoiceApi document endpoints', () => {
  it('downloads the invoice document as a file, not as a string', async () => {
    const c = mockClient();
    await useInvoiceApi(c as never).downloadPdf('inv-1');
    expect(c.download).toHaveBeenCalledWith('/invoices/inv-1/pdf');
    expect(c.get).not.toHaveBeenCalled();
  });

  it('reads the shareable address from the sibling pdf-url endpoint', async () => {
    const c = mockClient();
    const result = await useInvoiceApi(c as never).getPdfUrl('inv-1');
    expect(c.get).toHaveBeenCalledWith('/invoices/inv-1/pdf-url');
    expect(result.data).toBe('https://files.example/inv.pdf');
  });
});
