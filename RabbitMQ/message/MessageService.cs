using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace RabbitMQ.message;

public class MessageService : IMessageService, IDisposable
{
    private readonly RabbitMQOptions _options;
    private readonly ILogger<MessageService> _logger;
    private IConnection? _connection;
    private readonly ConcurrentDictionary<string, IChannel> _channels = new();
    private readonly ConcurrentDictionary<string, AsyncEventingBasicConsumer> _consumers = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    public MessageService(IOptions<RabbitMQOptions> options, ILogger<MessageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection?.IsOpen == true)
            return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_connection?.IsOpen == true)
                return;

            _connection?.Dispose();
            
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = _options.AutomaticRecovery,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds)
            };

            _connection = await ConnectWithRetryAsync(factory);
            _logger.LogInformation("RabbitMQ connection established to {Host}:{Port}", _options.HostName, _options.Port);
            
            _connection.ConnectionShutdownAsync += OnConnectionShutdown;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(ConnectionFactory factory)
    {
        var lastException = null as Exception;
        
        for (int i = 0; i < _options.RetryCount; i++)
        {
            try
            {
                return await factory.CreateConnectionAsync();
            }
            catch (BrokerUnreachableException ex)
            {
                lastException = ex;
                _logger.LogWarning("RabbitMQ connection attempt {Attempt} failed: {Message}", i + 1, ex.Message);
                
                if (i < _options.RetryCount - 1)
                {
                    await Task.Delay(_options.RetryDelayMilliseconds * (i + 1));
                }
            }
        }
        
        throw new Exception($"Failed to connect to RabbitMQ after {_options.RetryCount} attempts", lastException);
    }

    private Task OnConnectionShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning("RabbitMQ connection lost: {Reason}", e.ReplyText);
        _channels.Clear();
        return Task.CompletedTask;
    }

    private async Task<IChannel> GetOrCreateChannelAsync(string channelId)
    {
        await EnsureConnectionAsync();
        
        if (_channels.TryGetValue(channelId, out var existingChannel) && existingChannel.IsOpen)
            return existingChannel;
        
        var channel = await _connection!.CreateChannelAsync();
        channel.ChannelShutdownAsync += OnChannelShutdown;
        _channels[channelId] = channel;
        return channel;
    }

    private Task OnChannelShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning("RabbitMQ channel closed: {Reason}", e.ReplyText);
        return Task.CompletedTask;
    }

    private async Task EnsureQueueAsync(IChannel channel, string queueName, bool durable = true)
    {
        try
        {
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: durable,
                exclusive: false,
                autoDelete: false,
                arguments: null);
        }
        catch (AlreadyClosedException)
        {
        }
    }

    public async Task SendMessageAsync<T>(T message, string queueName)
    {
        var channelId = $"send-{queueName}";
        var channel = await GetOrCreateChannelAsync(channelId);
        
        bool queueDeclaringFailed = false;
        
        try
        {
            try
            {
                await channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                queueDeclaringFailed = true;
                _logger.LogDebug("Queue {Queue} already exists with different properties", queueName);
            }
            
            if (queueDeclaringFailed && !channel.IsOpen)
            {
                await channel.CloseAsync();
                _channels.TryRemove(channelId, out _);
                channel = await GetOrCreateChannelAsync(channelId);
            }
        }
        catch
        {
            _channels.TryRemove(channelId, out _);
            channel = await GetOrCreateChannelAsync(channelId);
        }

        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json"
            };

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: queueName,
                mandatory: false,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("Message sent to queue {Queue}: {Message}", queueName, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to queue {Queue}", queueName);
            _channels.TryRemove(channelId, out _);
            throw;
        }
    }

    public async Task SendMessageWithConfirmAsync<T>(T message, string queueName)
    {
        var channelId = $"send-confirm-{queueName}";
        var channel = await GetOrCreateChannelAsync(channelId);
        
        bool queueDeclaringFailed = false;
        
        try
        {
            try
            {
                await channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                queueDeclaringFailed = true;
                _logger.LogDebug("Queue {Queue} already exists with different properties", queueName);
            }
            
            if (queueDeclaringFailed && !channel.IsOpen)
            {
                await channel.CloseAsync();
                _channels.TryRemove(channelId, out _);
                channel = await GetOrCreateChannelAsync(channelId);
            }
        }
        catch
        {
            _channels.TryRemove(channelId, out _);
            channel = await GetOrCreateChannelAsync(channelId);
        }

        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json"
            };

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: queueName,
                mandatory: false,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("Message sent with confirmation to queue {Queue}: {Message}", queueName, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message with confirm to queue {Queue}", queueName);
            _channels.TryRemove(channelId, out _);
            throw;
        }
    }

    public async Task StartConsumingAsync(string queueName, Func<string, Task> messageHandler)
    {
        await EnsureConnectionAsync();
        
        var channel = await _connection!.CreateChannelAsync();
        
        bool queueDeclaringFailed = false;
        
        try
        {
            try
            {
                await channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                queueDeclaringFailed = true;
                _logger.LogDebug("Queue {Queue} already exists with different properties", queueName);
            }
            
            if (queueDeclaringFailed && !channel.IsOpen)
            {
                await channel.CloseAsync();
                channel.Dispose();
                channel = await _connection.CreateChannelAsync();
            }
        }
        catch
        {
            if (channel.IsOpen)
            {
                await channel.CloseAsync();
                channel.Dispose();
            }
            channel = await _connection.CreateChannelAsync();
        }

        try
        {
            await channel.BasicQosAsync(0, 10, false);

            var consumer = new AsyncEventingBasicConsumer(channel);
        
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
            
                _logger.LogInformation("Received message from queue {Queue}: {Message}", queueName, message);

                try
                {
                    await messageHandler(message);
                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from queue {Queue}", queueName);
                    await channel.BasicNackAsync(ea.DeliveryTag, false, true);
                }
            };

            _consumers[queueName] = consumer;
        
            await channel.BasicConsumeAsync(queueName, false, consumer);
            _logger.LogInformation("Started consuming queue {Queue}", queueName);
        }
        catch (Exception ex)
        {
            if (channel.IsOpen)
            {
                await channel.CloseAsync();
                channel.Dispose();
            }
            _logger.LogError(ex, "Failed to start consuming queue {Queue}", queueName);
            throw;
        }
    }

    public async Task StopConsumingAsync(string queueName)
    {
        if (_consumers.TryRemove(queueName, out _))
        {
            _logger.LogInformation("Stopped consuming queue {Queue}", queueName);
        }
        await Task.CompletedTask;
    }

    public async Task<int> GetQueueMessageCountAsync(string queueName)
    {
        var channel = await GetOrCreateChannelAsync($"count-{queueName}");
        var queueDeclareOk = await channel.QueueDeclarePassiveAsync(queueName);
        return (int)queueDeclareOk.MessageCount;
    }

    public async IAsyncEnumerable<string> ConsumeMessagesAsync(string queueName)
    {
        await EnsureConnectionAsync();
        
        var channel = await GetOrCreateChannelAsync($"stream-{queueName}");
        
        await EnsureQueueAsync(channel, queueName, true);

        var consumer = new AsyncEventingBasicConsumer(channel);
        
        var tcs = new TaskCompletionSource<string>();
        
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            tcs.TrySetResult(message);
        };

        await channel.BasicConsumeAsync(queueName, false, consumer);
        
        while (!_disposed)
        {
            var message = await tcs.Task;
            yield return message;
            tcs = new TaskCompletionSource<string>();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        foreach (var channel in _channels.Values)
        {
            try
            {
                if (channel.IsOpen)
                {
                    channel.CloseAsync().GetAwaiter().GetResult();
                }
                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing channel");
            }
        }
        _channels.Clear();

        if (_connection?.IsOpen == true)
        {
            _connection.CloseAsync().GetAwaiter().GetResult();
        }
        _connection?.Dispose();
        
        _connectionLock.Dispose();
        
        _logger.LogInformation("RabbitMQ MessageService disposed");
    }
}
