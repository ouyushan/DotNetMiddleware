using RabbitMQ.message;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<RabbitMQOptions>(
    builder.Configuration.GetSection(RabbitMQOptions.SectionName));

builder.Services.AddSingleton<IMessageService, MessageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

var messageService = app.Services.GetRequiredService<IMessageService>();
await messageService.StartConsumingAsync("demo-queue", async (message) =>
{
    Console.WriteLine($"Processing message: {message}");
});

app.Run();
