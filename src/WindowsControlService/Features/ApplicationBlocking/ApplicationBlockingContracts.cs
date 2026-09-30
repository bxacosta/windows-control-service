using System.ComponentModel.DataAnnotations;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.ApplicationBlocking;

public sealed record AddApplicationRequest(
    [Required][MaxLength(260)] string ExecutablePath,
    [Required][MaxLength(100)] string Name);

public sealed record SetApplicationEnabledRequest([Required] bool? Enabled);

public sealed record AddApplicationResponse(long Id);

/// <param name="State">
/// The platform's own answer, not a copy of it. Typed as the enum rather than as its name so
/// that the set of values crossing the API is discoverable: a response that carries a string
/// is a closed set the compiler cannot see and reflection cannot enumerate, which leaves the
/// browser comparing against words nothing checks. It goes out as the name either way --
/// <c>JsonStringEnumConverter</c> is registered with no naming policy, on the REST answer and
/// on the event stream alike.
/// </param>
/// <param name="LastReconciledAt">Null until the worker has completed one cycle.</param>
public sealed record PolicyStateResponse(PolicyState State, int EnabledRuleCount, DateTime? LastReconciledAt);
