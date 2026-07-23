namespace AttendanceQR.Domain.Enums;

// Deliberately NOT called TaskStatus — that name collides with System.Threading.Tasks.TaskStatus,
// which is in scope everywhere via the implicit global usings and would need qualifying at every site.
public enum TaskItemStatus
{
    Pending = 0,
    Completed = 1
}
