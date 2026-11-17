import type { ClaimCategory, ClaimStatus } from './api/types';

const nok = new Intl.NumberFormat('nb-NO', { style: 'currency', currency: 'NOK', maximumFractionDigits: 2 });
const date = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});
const dateTime = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

export const formatNok = (amount: number) => nok.format(amount);

/** Formats a date-only value (yyyy-MM-dd) without shifting it by the viewer's time zone. */
export const formatDate = (isoDate: string) => date.format(new Date(`${isoDate}T00:00:00Z`));

/** Formats a UTC timestamp in the viewer's local time. */
export const formatDateTime = (isoUtc: string) => dateTime.format(new Date(isoUtc));

export const formatPercent = (ratio: number | null) =>
  ratio === null ? '–' : `${Math.round(ratio * 100)} %`;

const statusLabels: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

export const statusLabel = (status: ClaimStatus) => statusLabels[status];

const categoryLabels: Record<ClaimCategory, string> = {
  Motor: 'Motor',
  Home: 'Home',
  Contents: 'Contents',
  Travel: 'Travel',
  Pet: 'Pet',
};

export const categoryLabel = (category: ClaimCategory) => categoryLabels[category];

/** Today's date as yyyy-MM-dd in the viewer's time zone, for date inputs. */
export function todayIso(): string {
  const now = new Date();
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}
