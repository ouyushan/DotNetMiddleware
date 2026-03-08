用于创建一个新的 .NET Web 项目并集成 RabbitMQ，实现消息的生产（发送）和消费（订阅）功能。

---

## 🎯 技术方案概述

- **框架**: ASP.NET Core Web API
- **消息队列**: RabbitMQ
- **客户端库**: RabbitMQ.Client
- **依赖注入**: 使用内置 DI 容器管理 RabbitMQ 客户端和服务
- **消息序列化**: JSON
- **功能**:
    - 生产者：通过 HTTP API 发送消息到 RabbitMQ
    - 消费者：后台服务持续监听队列并处理消息

---

## 📋 实现步骤

### 1. 创建项目

打开终端或命令提示符，执行：

```bash
dotnet new webapi -n RabbitMQ
cd RabbitMQ
```

### 2. 安装依赖包

```bash
dotnet add package RabbitMQ.Client
```

### 3. 配置 `appsettings.json`

添加 RabbitMQ 连接信息：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "QueueName": "my-queue"
  }
}
```

### 4. 创建消息服务接口和实现

#### `IMessageProducer.cs`

```csharp
public interface IMessageProducer
{
    void SendMessage<T>(T message);
}
```

#### `MessageProducer.cs`

```csharp
using RabbitMQ.Client;
using System.Text.Json;

public class MessageProducer : IMessageProducer
{
    private readonly string _queueName;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public MessageProducer(IConfiguration configuration)
    {
        var factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMQ:HostName"],
            Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = configuration["RabbitMQ:UserName"],
            Password = configuration["RabbitMQ:Password"],
            VirtualHost = configuration["RabbitMQ:VirtualHost"]
        };

        _queueName = configuration["RabbitMQ:QueueName"];
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(queue: _queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
    }

    public void SendMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = System.Text.Encoding.UTF8.GetBytes(json);

        _channel.BasicPublish(exchange: "", routingKey: _queueName, basicProperties: null, body: body);
    }
}
```

#### `IMessageConsumer.cs`

```csharp
public interface IMessageConsumer
{
    void Consume();
}
```

#### `MessageConsumer.cs`

```csharp
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

public class MessageConsumer : IMessageConsumer, IDisposable
{
    private readonly string _queueName;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<MessageConsumer> _logger;

    public MessageConsumer(IConfiguration configuration, ILogger<MessageConsumer> logger)
    {
        _logger = logger;
        var factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMQ:HostName"],
            Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = configuration["RabbitMQ:UserName"],
            Password = configuration["RabbitMQ:Password"],
            VirtualHost = configuration["RabbitMQ:VirtualHost"]
        };

        _queueName = configuration["RabbitMQ:QueueName"];
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(queue: _queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
    }

    public void Consume()
    {
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            _logger.LogInformation("Received message: {Message}", message);

            // 在这里处理你的业务逻辑
            ProcessMessage(message);

            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        };

        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
    }

    private void ProcessMessage(string message)
    {
        // 示例：反序列化消息并处理
        try
        {
            dynamic data = JsonSerializer.Deserialize<dynamic>(message);
            _logger.LogInformation("Processing data: {@Data}", data);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message: {Message}", message);
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
```

### 5. 注册服务

在 `Program.cs` 中注册服务：

```csharp
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSingleton<IMessageProducer, MessageProducer>();
builder.Services.AddSingleton<IMessageConsumer, MessageConsumer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Start consuming messages in the background
var consumer = app.Services.GetService<IMessageConsumer>();
consumer?.Consume();

app.Run();
```

### 6. 创建控制器

#### `MessageController.cs`

```csharp
[ApiController]
[Route("[controller]")]
public class MessageController : ControllerBase
{
    private readonly IMessageProducer _messageProducer;

    public MessageController(IMessageProducer messageProducer)
    {
        _messageProducer = messageProducer;
    }

    [HttpPost]
    public IActionResult Post([FromBody] dynamic data)
    {
        _messageProducer.SendMessage(data);
        return Ok(new { Message = "Message sent successfully!" });
    }
}
```

### 7. 启动 RabbitMQ 服务

确保 RabbitMQ 已启动，例如使用 Docker：

```bash
docker run -d --hostname my-rabbit --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

### 8. 运行应用

```bash
dotnet run
```

访问 `http://localhost:5000/message` 并 POST 一个 JSON 消息，例如：

```json
{
  "id": 1,
  "text": "Hello, RabbitMQ!"
}
```

你将在控制台日志中看到消息被成功接收和处理。

---

## ✅ 总结

- **生产者**：通过 HTTP API `/message` 发送消息到 RabbitMQ 队列。
- **消费者**：后台服务持续监听队列并处理消息。
- **依赖注入**：使用 DI 管理 RabbitMQ 客户端和服务实例。
- **异步处理**：消费者使用事件驱动模型处理消息。

---

## 📝 扩展建议

- **持久化消息**：设置队列为 `durable`，消息为 `persistent`。
- **多队列/Exchange**：使用 `Exchange` 和 `RoutingKey` 实现更复杂的路由。
- **错误处理**：添加重试、死信队列等。
- **后台服务**：使用 `BackgroundService` 替代在 `Program.cs` 中启动消费者。

希望这个方案对你有帮助！如有需要，我可以进一步优化或扩展功能。