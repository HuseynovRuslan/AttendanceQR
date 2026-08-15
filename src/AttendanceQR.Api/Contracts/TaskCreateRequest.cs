namespace AttendanceQR.Api.Contracts;

/// <summary>A new item for the shared task board.</summary>
public record TaskCreateRequest(string Title);

/// <summary>Rename a task (click-to-edit).</summary>
public record TaskTitleRequest(string Title);

/// <summary>Set or clear a task's due date. Null clears it ("Bu gün / Sabah / Tarix seç" all send a date).</summary>
public record TaskDueRequest(DateOnly? DueDate);

/// <summary>Give a task to a teammate, or hand it back to nobody (null).</summary>
public record TaskAssignRequest(Guid? EmployeeId);

/// <summary>The new order of the open list after a drag — every open id, top to bottom.</summary>
public record TaskReorderRequest(List<Guid> Ids);
