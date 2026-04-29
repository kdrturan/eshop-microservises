var builder = WebApplication.CreateBuilder(args);
var assembly = typeof(Program).Assembly;
var dbConnection = builder.Configuration.GetConnectionString("Database")!;

builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(assembly);
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
    configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(assembly);
builder.Services.AddCarter();
builder.Services.AddMarten(opts =>
{
    opts.Connection(dbConnection);

}).UseLightweightSessions();

if (builder.Environment.IsDevelopment())
{
    builder.Services.InitializeMartenWith<CatalogInitialData>();
}

builder.Services.AddExceptionHandler<CustomExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection);

var app = builder.Build();
    
app.MapCarter();
app.UseExceptionHandler(opts => { });

app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

app.Run();
