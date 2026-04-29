using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;


var builder = WebApplication.CreateBuilder(args);
var assembly = typeof(Program).Assembly;
var dbConnectionString = builder.Configuration.GetConnectionString("MartenDb")!;
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")!;

builder.Services.AddMediatR(config =>
{
    config.RegisterServicesFromAssemblies(assembly);
    config.AddOpenBehavior(typeof(ValidationBehavior<,>));
    config.AddOpenBehavior(typeof(LoggingBehavior<,>));
});

builder.Services.AddMarten(opts =>
{
    opts.Connection(dbConnectionString);
    opts.Schema.For<ShoppingCart>().Identity(x => x.UserName);
}).UseLightweightSessions();

builder.Services.AddCarter();

builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.Decorate<IBasketRepository, BasketRepository>();
builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = redisConnectionString;
    //opts.InstanceName = "Basket";
});
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnectionString)
    .AddRedis(redisConnectionString);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();
var app = builder.Build();
app.MapCarter();
app.UseExceptionHandler();
app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
app.Run();


