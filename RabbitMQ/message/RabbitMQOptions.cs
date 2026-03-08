namespace RabbitMQ.message;

public class RabbitMQOptions
{
    public const string SectionName = "RabbitMQ";
    
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string QueueName { get; set; } = "demo-queue";
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    public bool AutomaticRecovery { get; set; } = true;
    public int NetworkRecoveryIntervalSeconds { get; set; } = 10;
}
