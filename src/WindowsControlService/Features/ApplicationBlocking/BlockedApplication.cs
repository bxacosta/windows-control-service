using System.ComponentModel.DataAnnotations;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.ApplicationBlocking;

/// <summary>
/// The WDAC attributes a deny rule may match on. Named "Field" rather than "Attribute" only
/// because CA1711 reserves that suffix for <see cref="Attribute"/> types.
/// <para>
/// Every member name here <b>is</b> the XML attribute name written into the policy, which is
/// why they are not renamed for style: what is valid is decided by the schema shipped with
/// Windows, not by this file.
/// </para>
/// </summary>
/// <remarks>
/// <c>FileName</c> is the trap worth remembering: it does not compare against the name of the
/// file on disk, it compares against the OriginalFilename embedded in the binary.
/// </remarks>
public enum RuleMatchField
{
    FileName,
    InternalName,
    ProductName,
}

public sealed class BlockedApplication
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// The WDAC attribute the deny rule matches on: <c>FileName</c>, <c>InternalName</c> or
    /// <c>ProductName</c>. Not always the first one, because not every binary carries an
    /// OriginalFilename.
    /// </summary>
    public RuleMatchField MatchAttribute { get; set; } = RuleMatchField.FileName;

    /// <summary>
    /// The value read out of the PE version resource. Stored rather than read on demand so the
    /// policy can be rebuilt without touching the disk, and exposed by the API because it
    /// explains why renaming the executable does not defeat the block.
    /// </summary>
    public string MatchValue { get; set; } = string.Empty;

    public string? ProductName { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}

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
