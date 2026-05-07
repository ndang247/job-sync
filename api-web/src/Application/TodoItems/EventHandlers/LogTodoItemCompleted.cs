using api_web.Domain.Events;
using Microsoft.Extensions.Logging;

namespace api_web.Application.TodoItems.EventHandlers;

public class LogTodoItemCompleted : INotificationHandler<TodoItemCompletedEvent>
{
    private readonly ILogger<LogTodoItemCompleted> _logger;

    public LogTodoItemCompleted(ILogger<LogTodoItemCompleted> logger)
    {
        _logger = logger;
    }

    public Task Handle(TodoItemCompletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("api_web Domain Event: {DomainEvent}", notification.GetType().Name);

        return Task.CompletedTask;
    }
}
