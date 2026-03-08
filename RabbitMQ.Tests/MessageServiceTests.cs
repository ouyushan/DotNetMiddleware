using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.message;

namespace RabbitMQ.Tests;

public class MessageServiceTests
{
    private readonly Mock<IOptions<RabbitMQOptions>> _optionsMock;
    private readonly Mock<ILogger<MessageService>> _loggerMock;
    private readonly RabbitMQOptions _options;

    public MessageServiceTests()
    {
        _options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            QueueName = "test-queue",
            RetryCount = 3,
            RetryDelayMilliseconds = 100,
            AutomaticRecovery = true,
            NetworkRecoveryIntervalSeconds = 10
        };
        
        _optionsMock = new Mock<IOptions<RabbitMQOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(_options);
        _loggerMock = new Mock<ILogger<MessageService>>();
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldCreateInstance()
    {
        var service = new MessageService(_optionsMock.Object, _loggerMock.Object);
        
        Assert.NotNull(service);
    }

    [Fact]
    public void RabbitMQOptions_DefaultValues_ShouldBeSet()
    {
        var options = new RabbitMQOptions();
        
        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal("demo-queue", options.QueueName);
        Assert.True(options.AutomaticRecovery);
    }

    [Fact]
    public void RabbitMQOptions_CustomValues_ShouldBeSet()
    {
        var options = new RabbitMQOptions
        {
            HostName = "rabbitmq.example.com",
            Port = 5673,
            UserName = "admin",
            Password = "admin123",
            VirtualHost = "/myvhost",
            QueueName = "custom-queue",
            RetryCount = 5,
            AutomaticRecovery = false
        };
        
        Assert.Equal("rabbitmq.example.com", options.HostName);
        Assert.Equal(5673, options.Port);
        Assert.Equal("admin", options.UserName);
        Assert.Equal("admin123", options.Password);
        Assert.Equal("/myvhost", options.VirtualHost);
        Assert.Equal("custom-queue", options.QueueName);
        Assert.Equal(5, options.RetryCount);
        Assert.False(options.AutomaticRecovery);
    }

    [Fact]
    public void RabbitMQOptions_SectionName_ShouldBeCorrect()
    {
        Assert.Equal("RabbitMQ", RabbitMQOptions.SectionName);
    }

    [Fact]
    public void MessageService_Dispose_ShouldNotThrow()
    {
        var service = new MessageService(_optionsMock.Object, _loggerMock.Object);
        
        var exception = Record.Exception(() => service.Dispose());
        
        Assert.Null(exception);
    }

    [Fact]
    public void MessageService_Dispose_CanBeCalledMultipleTimes()
    {
        var service = new MessageService(_optionsMock.Object, _loggerMock.Object);
        
        service.Dispose();
        service.Dispose();
        
        Assert.True(true);
    }
}

public class MessageDtoTests
{
    [Fact]
    public void MessageDto_WithProperties_ShouldWork()
    {
        var dto = new MessageDto
        {
            Id = Guid.NewGuid().ToString(),
            Content = "Test message",
            Timestamp = DateTime.UtcNow
        };
        
        Assert.NotNull(dto.Id);
        Assert.Equal("Test message", dto.Content);
    }

    [Fact]
    public void MessageDto_EmptyConstructor_ShouldWork()
    {
        var dto = new MessageDto();
        
        Assert.NotNull(dto.Id);
        Assert.Equal(string.Empty, dto.Content);
    }
}

public class IMessageServiceTests
{
    [Fact]
    public void IMessageService_ShouldDefineRequiredMethods()
    {
        var type = typeof(IMessageService);
        
        Assert.True(type.IsInterface);
        
        var methods = type.GetMethods();
        Assert.NotEmpty(methods);
        
        var sendMethod = methods.FirstOrDefault(m => m.Name == nameof(IMessageService.SendMessageAsync));
        Assert.NotNull(sendMethod);
        
        var consumeMethod = methods.FirstOrDefault(m => m.Name == nameof(IMessageService.StartConsumingAsync));
        Assert.NotNull(consumeMethod);
    }
}

public class ConnectionFactoryTests
{
    [Fact]
    public void ConnectionFactory_DefaultConfiguration_ShouldWork()
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };
        
        Assert.Equal("localhost", factory.HostName);
        Assert.Equal(5672, factory.Port);
        Assert.Equal("guest", factory.UserName);
        Assert.Equal("guest", factory.Password);
        Assert.Equal("/", factory.VirtualHost);
        Assert.True(factory.AutomaticRecoveryEnabled);
    }

    [Fact]
    public void ConnectionFactory_WithRetrySettings_ShouldWork()
    {
        var options = new RabbitMQOptions
        {
            RetryCount = 5,
            RetryDelayMilliseconds = 2000,
            AutomaticRecovery = true,
            NetworkRecoveryIntervalSeconds = 15
        };
        
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = options.AutomaticRecovery,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(options.NetworkRecoveryIntervalSeconds)
        };
        
        Assert.Equal(15, factory.NetworkRecoveryInterval.TotalSeconds);
        Assert.True(factory.AutomaticRecoveryEnabled);
    }
}
