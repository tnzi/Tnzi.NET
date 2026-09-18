import { describe, it, expect, vi } from 'vitest';
import { filesToContentParts, AttachmentRefusedError } from '../../src/headless/attachment-parts';

function makeFile(name: string, type: string, bytes: number[] = [1, 2, 3]): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

/**
 * What a composer collects (`File[]`) and what the chat endpoint accepts
 * (`ContentPartDto[]`) are two shapes; this is the one place that converts.
 * Both built-in transports used to drop the files on the floor between them.
 */
describe('filesToContentParts', () => {
  it('returns no parts for a text-only turn', async () => {
    const result = await filesToContentParts('hello', []);
    expect(result.parts).toBeNull();
    expect(result.attachments).toEqual([]);
  });

  it('inlines an image as a base64 image part, after the text part', async () => {
    const result = await filesToContentParts('what is this?', [makeFile('a.png', 'image/png', [0x89, 0x50, 0x4e, 0x47])]);
    expect(result.parts).toEqual([
      { type: 'text', text: 'what is this?' },
      { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png' },
    ]);
    // The user row renders the same bytes as a thumbnail.
    expect(result.attachments).toEqual([
      { type: 'image', fileName: 'a.png', mediaType: 'image/png', base64Data: 'iVBORw==' },
    ]);
  });

  it('carries the trimmed text as the wire message', async () => {
    const result = await filesToContentParts('  what is this?  ', [makeFile('a.png', 'image/png')]);
    expect(result.message).toBe('what is this?');
  });

  /**
   * The backend adds the user turn to the model input and persists it only
   * when `message` is non-blank; the content parts are never consulted on
   * their own. A files-only turn therefore needs a message the user did not
   * type: the attachment names. The same text leads the parts, so the model
   * also sees what was attached and a provider that rejects image-only input
   * gets a text part.
   */
  it('names the attachments as the message when the turn is files only', async () => {
    const result = await filesToContentParts('   ', [makeFile('a.png', 'image/png'), makeFile('b.png', 'image/png')]);
    expect(result.message).toBe('[Attached: a.png, b.png]');
    expect(result.parts?.[0]).toEqual({ type: 'text', text: '[Attached: a.png, b.png]' });
    expect(result.parts?.map((p) => p.type)).toEqual(['text', 'image', 'image']);
  });

  it('a text-only turn keeps the trimmed text as its message', async () => {
    const result = await filesToContentParts(' hi ', []);
    expect(result.message).toBe('hi');
  });

  it('references a non-image file through the upload hook', async () => {
    const uploadFile = vi.fn(async (file: File) => ({ id: 'f-1', fileName: file.name }));
    const result = await filesToContentParts('summarise', [makeFile('doc.pdf', 'application/pdf')], { uploadFile });
    expect(uploadFile).toHaveBeenCalledTimes(1);
    expect(result.parts).toEqual([
      { type: 'text', text: 'summarise' },
      { type: 'file', fileId: 'f-1', fileName: 'doc.pdf' },
    ]);
    expect(result.attachments).toEqual([{ type: 'file', fileId: 'f-1', fileName: 'doc.pdf' }]);
  });

  /**
   * The chat endpoint has no inline representation for a non-image file - a
   * `file` part is a Storage id. Without an uploader the honest answer is a
   * refusal the user sees, not a request that quietly leaves the file behind.
   */
  it('refuses a non-image file when no upload hook is configured', async () => {
    await expect(filesToContentParts('x', [makeFile('doc.pdf', 'application/pdf')])).rejects.toBeInstanceOf(
      AttachmentRefusedError,
    );
    await expect(filesToContentParts('x', [makeFile('doc.pdf', 'application/pdf')])).rejects.toThrow(/doc\.pdf/);
  });

  it('refuses an oversized attachment before anything is read or uploaded', async () => {
    const uploadFile = vi.fn();
    const big = makeFile('big.png', 'image/png', new Array(64).fill(0));
    await expect(filesToContentParts('x', [big], { uploadFile, maxBytes: 32 })).rejects.toThrow(/big\.png/);
    expect(uploadFile).not.toHaveBeenCalled();
  });

  it('surfaces an upload failure as a refusal', async () => {
    const uploadFile = vi.fn(async () => {
      throw new Error('storage down');
    });
    await expect(
      filesToContentParts('x', [makeFile('doc.pdf', 'application/pdf')], { uploadFile }),
    ).rejects.toThrow('storage down');
  });
});
