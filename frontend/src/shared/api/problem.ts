import { t } from '@/shared/i18n';

/** RFC 9457 Problem Details as returned by the API (with our `code` extension). */
export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | string | null;
  detail?: string | null;
  code?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get code(): string | undefined {
    return this.problem?.code;
  }
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'status' in value);
}

/** User-facing (es-AR) message for any error thrown by the API layer. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return (error.code && t.errors.codes[error.code]) || t.errors.generic;
  }
  if (error instanceof TypeError) {
    return t.errors.network;
  }
  return t.errors.generic;
}
