import type { ClaimCategory, Policy } from '../api/types';
import { categoryLabel, formatDate, formatNok } from '../format';

export interface FormValues {
  policyNumber: string;
  category: ClaimCategory | '';
  incidentDate: string;
  claimedAmount: string;
  description: string;
}

export type FieldErrors = Partial<Record<keyof FormValues, string>>;

export const emptyForm: FormValues = {
  policyNumber: '',
  category: '',
  incidentDate: '',
  claimedAmount: '',
  description: '',
};

/**
 * Quick checks that mirror the API's rules, so most mistakes are caught before a request is sent.
 * The API remains the authority: anything it rejects is shown next to the same fields.
 */
export function validateClaimForm(
  values: FormValues,
  policy: Policy | undefined,
  today: string,
): FieldErrors {
  const errors: FieldErrors = {};
  const amount = Number(values.claimedAmount.replace(',', '.'));

  if (!values.policyNumber) errors.policyNumber = 'Choose a policy.';
  if (!values.category) errors.category = 'Choose a category.';
  else if (policy && values.category !== policy.coverageCategory)
    errors.category = `This policy covers ${categoryLabel(policy.coverageCategory)} claims only.`;

  if (!values.incidentDate) errors.incidentDate = 'Enter the date of the incident.';
  else if (values.incidentDate > today) errors.incidentDate = 'Incident date cannot be in the future.';
  else if (policy && (values.incidentDate < policy.validFrom || values.incidentDate > policy.validTo))
    errors.incidentDate = `Must be within the policy period (${formatDate(policy.validFrom)} – ${formatDate(policy.validTo)}).`;

  if (!values.claimedAmount) errors.claimedAmount = 'Enter the amount claimed.';
  else if (!Number.isFinite(amount) || amount < 1) errors.claimedAmount = 'Amount must be at least 1 NOK.';
  else if (Math.round(amount * 100) !== amount * 100)
    errors.claimedAmount = 'Use at most two decimal places.';
  else if (policy && amount > policy.coverageLimit)
    errors.claimedAmount = `Exceeds the coverage limit of ${formatNok(policy.coverageLimit)}.`;

  const description = values.description.trim();
  if (description.length < 10) errors.description = 'Describe what happened (at least 10 characters).';
  else if (description.length > 1000) errors.description = 'Keep the description under 1000 characters.';

  return errors;
}
