using Microsoft.AspNetCore.Mvc;
using RabbitMQ.message;

namespace RabbitMQ.Controller;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, ILogger<MessagesController> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> SendMessageAsync()
    {
        var messageDto = new MessageDto
        {
            Id = Guid.NewGuid().ToString(),
            Content = "publish a message",
            Timestamp = DateTime.UtcNow
        };
        var queueName = "demo-queue";
        await _messageService.SendMessageAsync(messageDto,queueName);
        return Ok(new { message = "RabbitMQ integration is working!" });
    }
}