using System.ComponentModel.DataAnnotations;

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

/// <param name="LastReconciledAt">Null until the worker has completed one cycle.</param>
public sealed record PolicyStateResponse(string State, int EnabledRuleCount, DateTime? LastReconciledAt);
