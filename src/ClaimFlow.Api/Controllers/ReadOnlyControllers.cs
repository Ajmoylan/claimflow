using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Controllers;

/// <summary>The fictional policies that claims can be filed against. Read-only.</summary>
[ApiController]
[Route("api/policies")]
public sealed class PoliciesController(PolicyService policies) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PolicyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PolicyDto>>> List(CancellationToken cancellationToken) =>
        Ok(await policies.ListAsync(cancellationToken));
}

/// <summary>Summary figures for the dashboard.</summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken) =>
        Ok(await dashboard.GetSummaryAsync(cancellationToken));
}
