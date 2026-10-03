using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace BricksNet.EntityFrameworkCore.ValueGeneration.Internal;

/// <summary>Generates version 7 (time-ordered) GUIDs.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class GuidV7ValueGenerator : ValueGenerator<Guid>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
