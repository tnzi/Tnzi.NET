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
      { type: 'image', base64Data: 'iVBORw==', mediaType: 'image/png', fileName: 'a.png' },
    ]);
    // The user row renders the same bytes as a thumbnail.
    expect(result.attachments).toEqual([
      { type: 'image', fileName: 'a.png', mediaType: 'image/png', base64Data: 'iVBORw==', size: 4 },
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
    const result = await filesToContentParts('summarise', [makeFile('notes.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')], { uploadFile });
    expect(uploadFile).toHaveBeenCalledTimes(1);
    expect(result.parts).toEqual([
      { type: 'text', text: 'summarise' },
      { type: 'file', fileId: 'f-1', fileName: 'notes.docx' },
    ]);
    expect(result.attachments).toEqual([{ type: 'file', fileId: 'f-1', fileName: 'notes.docx' }]);
  });

  /**
   * The chat endpoint has no inline representation for a non-image file - a
   * `file` part is a Storage id. Without an uploader the honest answer is a
   * refusal the user sees, not a request that quietly leaves the file behind.
   */
  it('refuses a non-image file when no upload hook is configured', async () => {
    await expect(filesToContentParts('x', [makeFile('notes.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')])).rejects.toBeInstanceOf(
      AttachmentRefusedError,
    );
    await expect(filesToContentParts('x', [makeFile('notes.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')])).rejects.toThrow(/notes\.docx/);
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
      filesToContentParts('x', [makeFile('notes.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')], { uploadFile }),
    ).rejects.toThrow('storage down');
  });

  /**
   * An agent that runs as an external CLI opens files with its own tools; the
   * backend takes them inline on turns it accepts files for.
   */
  describe('inlineFiles', () => {
    it('sends any other file inline as a document part with its name and type', async () => {
      const uploadFile = vi.fn();
      const result = await filesToContentParts('what does it want?', [makeFile('invoice.eml', 'message/rfc822', [0x46, 0x72])], {
        inlineFiles: true,
        uploadFile,
      });

      expect(result.parts).toEqual([
        { type: 'text', text: 'what does it want?' },
        { type: 'document', base64Data: 'RnI=', mediaType: 'message/rfc822', fileName: 'invoice.eml' },
      ]);
      expect(result.attachments).toEqual([
        { type: 'file', fileName: 'invoice.eml', mediaType: 'message/rfc822', base64Data: 'RnI=', size: 2 },
      ]);
      expect(uploadFile).not.toHaveBeenCalled();
    });

    it('a file the browser gave no type is sent as application/octet-stream', async () => {
      const result = await filesToContentParts('', [makeFile('build.log', '')], { inlineFiles: true });
      expect(result.parts?.[1]).toMatchObject({ type: 'document', mediaType: 'application/octet-stream', fileName: 'build.log' });
      expect(result.message).toBe('[Attached: build.log]');
    });

    it('images and PDFs keep their own parts', async () => {
      const result = await filesToContentParts('x', [makeFile('a.png', 'image/png'), makeFile('b.pdf', 'application/pdf')], { inlineFiles: true });
      expect(result.parts?.slice(1).map((p) => [p.type, (p as { mediaType?: string }).mediaType])).toEqual([
        ['image', 'image/png'],
        ['document', 'application/pdf'],
      ]);
    });

    it('files have their own size ceiling, images keep theirs', async () => {
      const big = makeFile('data.csv', 'text/csv', new Array(20).fill(1));
      await expect(filesToContentParts('x', [big], { inlineFiles: true, maxBytes: 10, maxFileBytes: 30 })).resolves.toBeTruthy();
      await expect(filesToContentParts('x', [big], { inlineFiles: true, maxBytes: 10, maxFileBytes: 15 })).rejects.toMatchObject({
        reason: 'too-large',
        fileName: 'data.csv',
      });
      const image = makeFile('a.png', 'image/png', new Array(20).fill(1));
      await expect(filesToContentParts('x', [image], { inlineFiles: true, maxBytes: 10, maxFileBytes: 30 })).rejects.toBeInstanceOf(
        AttachmentRefusedError,
      );
    });
  });
});
