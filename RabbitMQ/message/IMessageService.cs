namespace RabbitMQ.message;

public interface IMessageService
{
    Task SendMessageAsync<T>(T message, string queueName);
    Task StartConsumingAsync(string queueName, Func<string,Task> messageHandler);
}