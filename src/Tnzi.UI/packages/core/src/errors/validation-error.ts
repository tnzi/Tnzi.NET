/**
 * @tnzi/core/errors/validation-error
 *
 * Validation error class.
 */

export class ValidationError extends Error {
  constructor(
    message: string,
    public field?: string,
    public errors?: Record<string, string[]>
  ) {
    super(message);
    this.name = 'ValidationError';
  }

  override toString(): string {
    const parts: string[] = [`ValidationError: ${this.message}`];
    if (this.field) {
      parts.push(`  Field: ${this.field}`);
    }
    if (this.errors) {
      for (const [field, messages] of Object.entries(this.errors)) {
        for (const msg of messages) {
          parts.push(`  ${field}: ${msg}`);
        }
      }
    }
    return parts.join('\n');
  }
}

