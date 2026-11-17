import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { ApiError, api } from '../api/client';
import { CLAIM_CATEGORIES, type ClaimCategory, type CreateClaimRequest, type Policy } from '../api/types';
import { ErrorMessage, Loading } from '../components/ui';
import { emptyForm, validateClaimForm, type FieldErrors, type FormValues } from './claimFormValidation';
import { categoryLabel, formatDate, formatNok, todayIso } from '../format';

export function NewClaimPage() {
  const policies = useQuery({ queryKey: ['policies'], queryFn: api.getPolicies, staleTime: Infinity });

  if (policies.isPending) return <Loading />;
  if (policies.isError) return <ErrorMessage error={policies.error} onRetry={() => policies.refetch()} />;
  return <NewClaimForm policies={policies.data} />;
}

export function NewClaimForm({ policies }: { policies: Policy[] }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [values, setValues] = useState<FormValues>(emptyForm);
  const [errors, setErrors] = useState<FieldErrors>({});
  const policy = policies.find((p) => p.policyNumber === values.policyNumber);

  const create = useMutation({
    mutationFn: (body: CreateClaimRequest) => api.createClaim(body),
    onSuccess: (claim) => {
      void queryClient.invalidateQueries({ queryKey: ['claims'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.setQueryData(['claim', claim.reference], claim);
      navigate(`/claims/${claim.reference}`);
    },
    onError: (error) => {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        const fromServer: FieldErrors = {};
        for (const field of Object.keys(emptyForm) as (keyof FormValues)[]) {
          const message = error.errorsFor(field)[0];
          if (message) fromServer[field] = message;
        }
        setErrors(fromServer);
      }
    },
  });

  const set = (patch: Partial<FormValues>) => setValues((current) => ({ ...current, ...patch }));

  const choosePolicy = (policyNumber: string) => {
    const chosen = policies.find((p) => p.policyNumber === policyNumber);
    // Pre-select the only category the policy covers.
    set({ policyNumber, category: chosen?.coverageCategory ?? values.category });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const found = validateClaimForm(values, policy, todayIso());
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    create.mutate({
      policyNumber: values.policyNumber,
      category: values.category as ClaimCategory,
      incidentDate: values.incidentDate,
      claimedAmount: Number(values.claimedAmount.replace(',', '.')),
      description: values.description.trim(),
    });
  };

  const apiError = create.error instanceof ApiError ? create.error : null;
  const showGeneralError = create.isError && (!apiError || Object.keys(apiError.fieldErrors).length === 0);

  const fieldProps = (name: keyof FormValues) => ({
    id: name,
    name,
    'aria-invalid': errors[name] ? true : undefined,
    'aria-describedby': errors[name] ? `${name}-error` : undefined,
  });
  const fieldError = (name: keyof FormValues) =>
    errors[name] && (
      <p className="field-error" id={`${name}-error`}>
        {errors[name]}
      </p>
    );

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Submit a claim</h1>
          <p className="muted">
            Use one of the fictional DEMO policies. Please don&apos;t enter real personal information.
          </p>
        </div>
      </div>

      <form className="card form" onSubmit={submit} noValidate aria-label="New claim">
        <div className="field">
          <label htmlFor="policyNumber">Policy</label>
          <select
            {...fieldProps('policyNumber')}
            value={values.policyNumber}
            onChange={(e) => choosePolicy(e.target.value)}
          >
            <option value="">Choose a policy…</option>
            {policies.map((p) => (
              <option key={p.policyNumber} value={p.policyNumber}>
                {p.policyNumber} · {categoryLabel(p.coverageCategory)} · {p.holderName}
                {p.isActive ? '' : ' (expired)'}
              </option>
            ))}
          </select>
          {policy && (
            <p className="hint">
              Covers {categoryLabel(policy.coverageCategory)} up to {formatNok(policy.coverageLimit)}, valid{' '}
              {formatDate(policy.validFrom)} – {formatDate(policy.validTo)}.
            </p>
          )}
          {fieldError('policyNumber')}
        </div>

        <div className="field">
          <label htmlFor="category">Category</label>
          <select
            {...fieldProps('category')}
            value={values.category}
            onChange={(e) => set({ category: e.target.value as ClaimCategory | '' })}
          >
            <option value="">Choose a category…</option>
            {CLAIM_CATEGORIES.map((c) => (
              <option key={c} value={c}>
                {categoryLabel(c)}
              </option>
            ))}
          </select>
          {fieldError('category')}
        </div>

        <div className="form-row">
          <div className="field">
            <label htmlFor="incidentDate">Incident date</label>
            <input
              {...fieldProps('incidentDate')}
              type="date"
              max={todayIso()}
              value={values.incidentDate}
              onChange={(e) => set({ incidentDate: e.target.value })}
            />
            {fieldError('incidentDate')}
          </div>
          <div className="field">
            <label htmlFor="claimedAmount">Amount claimed (NOK)</label>
            <input
              {...fieldProps('claimedAmount')}
              inputMode="decimal"
              placeholder="e.g. 12500"
              value={values.claimedAmount}
              onChange={(e) => set({ claimedAmount: e.target.value })}
            />
            {fieldError('claimedAmount')}
          </div>
        </div>

        <div className="field">
          <label htmlFor="description">What happened?</label>
          <textarea
            {...fieldProps('description')}
            rows={5}
            maxLength={1000}
            value={values.description}
            onChange={(e) => set({ description: e.target.value })}
          />
          <p className="hint">{values.description.trim().length} / 1000 characters</p>
          {fieldError('description')}
        </div>

        {showGeneralError && <ErrorMessage error={create.error} />}

        <div className="actions">
          <button type="submit" className="button" disabled={create.isPending}>
            {create.isPending ? 'Submitting…' : 'Submit claim'}
          </button>
        </div>
      </form>
    </>
  );
}
