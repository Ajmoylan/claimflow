import type { ReactNode } from 'react';
import { ApiError } from '../api/client';
import type { ClaimStatus } from '../api/types';
import { statusLabel } from '../format';

export function StatusBadge({ status }: { status: ClaimStatus }) {
  return <span className={`badge badge-${status}`}>{statusLabel(status)}</span>;
}

export function StatCard({ label, value, hint }: { label: string; value: ReactNode; hint?: string }) {
  return (
    <div className="card stat-card">
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      {hint && <div className="stat-hint">{hint}</div>}
    </div>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="loading" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      {label}
    </div>
  );
}

/** Shows an API error in plain language. Rate limits and server errors get a specific explanation. */
export function ErrorMessage({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const apiError = error instanceof ApiError ? error : null;
  let message = apiError?.detail ?? apiError?.title ?? 'Something went wrong.';
  if (apiError?.status === 429 && !apiError.detail) {
    message = 'Too many requests. Please wait a moment and try again.';
  }
  if (apiError && apiError.status >= 500) {
    message =
      'The server could not complete the request. If the demo database was asleep, retrying usually helps.';
  }

  return (
    <div className="alert alert-error" role="alert">
      <div>
        <strong>{apiError?.title ?? 'Error'}</strong>
        <p>{message}</p>
      </div>
      {onRetry && (
        <button type="button" className="button button-secondary" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}

export function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number;
  totalPages: number;
  onChange: (page: number) => void;
}) {
  if (totalPages <= 1) {
    return null;
  }
  return (
    <nav className="pagination" aria-label="Pagination">
      <button
        type="button"
        className="button button-secondary"
        disabled={page <= 1}
        onClick={() => onChange(page - 1)}
      >
        Previous
      </button>
      <span>
        Page {page} of {totalPages}
      </span>
      <button
        type="button"
        className="button button-secondary"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
      >
        Next
      </button>
    </nav>
  );
}
