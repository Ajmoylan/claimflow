using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Controllers;

/// <summary>Claims: search, view, create and move through the workflow.</summary>
[ApiController]
[Route("api/claims")]
public sealed class ClaimsController(ClaimService claims) : ControllerBase
{
    /// <summary>Search and filter claims, newest first by default.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ClaimSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ClaimSummaryDto>>> Search([FromQuery] ClaimQuery query, CancellationToken cancellationToken) =>
        Ok(await claims.SearchAsync(query, cancellationToken));

    /// <summary>Get one claim with its audit history and the statuses it can move to next.</summary>
    [HttpGet("{reference}", Name = nameof(GetClaim))]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimDetailDto>> GetClaim(string reference, CancellationToken cancellationToken) =>
        Ok(await claims.GetAsync(reference, cancellationToken));

    /// <summary>Get only the audit history of a claim, oldest first.</summary>
    [HttpGet("{reference}/history")]
    [ProducesResponseType<IReadOnlyList<StatusChangeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StatusChangeDto>>> GetHistory(string reference, CancellationToken cancellationToken) =>
        Ok(await claims.GetHistoryAsync(reference, cancellationToken));

    /// <summary>Submit a new claim against an existing fictional policy.</summary>
    [HttpPost]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ClaimDetailDto>> Create(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        var created = await claims.CreateAsync(request, cancellationToken);
        return CreatedAtRoute(nameof(GetClaim), new { reference = created.Reference }, created);
    }

    /// <summary>Move a claim to its next status. Allowed: Submitted → UnderReview → Approved or Rejected (a reason is required to reject).</summary>
    [HttpPost("{reference}/transitions")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDetailDto>> Transition(string reference, TransitionClaimRequest request, CancellationToken cancellationToken) =>
        Ok(await claims.TransitionAsync(reference, request, cancellationToken));
}
