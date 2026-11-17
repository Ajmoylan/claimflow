import type {
  ClaimDetail,
  ClaimQuery,
  ClaimSummary,
  CreateClaimRequest,
  DashboardSummary,
  PagedResult,
  Policy,
  TransitionRequest,
} from './types';

/**
 * An error response from the API, parsed from RFC 9457 ProblemDetails.
 * `fieldErrors` holds validation messages keyed by field name, so forms can show them next to the input.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail?: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, title: string, detail?: string, fieldErrors: Record<string, string[]> = {}) {
    super(detail ?? title);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.fieldErrors = fieldErrors;
  }

  /** Validation messages for a field, matched case-insensitively (the API uses PascalCase keys). */
  errorsFor(field: string): string[] {
    const key = Object.keys(this.fieldErrors).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? (this.fieldErrors[key] ?? []) : [];
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}) },
    });
  } catch {
    throw new ApiError(
      0,
      'Network error',
      'Could not reach the CLAIMFLOW API. Check your connection and try again.',
    );
  }

  if (response.ok) {
    return (await response.json()) as T;
  }

  let problem: { title?: string; detail?: string; errors?: Record<string, string[]> } = {};
  try {
    problem = await response.json();
  } catch {
    // Not a JSON body; fall back to the status text below.
  }
  throw new ApiError(
    response.status,
    problem.title ?? response.statusText ?? 'Request failed',
    problem.detail,
    problem.errors ?? {},
  );
}

function toQueryString(query: ClaimQuery): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }
  const text = params.toString();
  return text ? `?${text}` : '';
}

export const api = {
  getDashboard: () => request<DashboardSummary>('/api/dashboard/summary'),
  getPolicies: () => request<Policy[]>('/api/policies'),
  searchClaims: (query: ClaimQuery) =>
    request<PagedResult<ClaimSummary>>(`/api/claims${toQueryString(query)}`),
  getClaim: (reference: string) => request<ClaimDetail>(`/api/claims/${encodeURIComponent(reference)}`),
  createClaim: (body: CreateClaimRequest) =>
    request<ClaimDetail>('/api/claims', { method: 'POST', body: JSON.stringify(body) }),
  transitionClaim: (reference: string, body: TransitionRequest) =>
    request<ClaimDetail>(`/api/claims/${encodeURIComponent(reference)}/transitions`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
};
