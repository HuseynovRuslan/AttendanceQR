namespace AttendanceQR.Api.Contracts;

/// <summary>Create or rename a structure. Validated in the controller — no [property:] attributes on records
/// (see ScanRequest for why).</summary>
public record StructureRequest(string? Name);

/// <summary>Put these branches into one structure, moving them from wherever they were. A null structure
/// takes them out of any structure.</summary>
public record AssignStructureRequest(Guid? StructureId, IReadOnlyList<Guid>? LocationIds);
