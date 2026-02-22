/**
 * @tnzi/core/errors/business-error
 *
 * Business error class.
 */

export class BusinessError extends Error {
  constructor(
    message: string,
    public code?: string,
    public statusCode?: number
  ) {
    super(message);
    this.name = 'BusinessError';
  }
}

