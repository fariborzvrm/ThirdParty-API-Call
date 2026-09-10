using InquiryService.Api.Controllers;
using InquiryService.Api.Infrastructure;
using InquiryService.Api.Options;
using InquiryService.Api.Providers;
using InquiryService.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection("Cache"));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection("Providers"));

var connectionString = builder.Configuration.GetConnectionString("InquiryDb")
    ?? "Server=localhost,1433;Database=InquiryService;User Id=sa;Password=InquiryService!StrongPass1;TrustServerCertificate=True;Encrypt=False";
builder.Services.AddDbContext<InquiryDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<KeyedLocks>();
builder.Services.AddSingleton<InquiryResultCache>();
builder.Services.AddSingleton<FakeProviderRuntime>();
builder.Services.AddSingleton<IInquiryProvider>(sp => CreateFakeProvider(sp, "Alpha"));
builder.Services.AddSingleton<IInquiryProvider>(sp => CreateFakeProvider(sp, "Beta"));
builder.Services.AddSingleton<FailoverRunner>();
builder.Services.AddScoped<InquirySubmissionService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (Environment.GetEnvironmentVariable("RUN_MIGRATIONS") == "1")
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InquiryDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
    await DbInitializer.InitializeAsync(db, migrationLogger, CancellationToken.None);
}

app.Run();

static FakeProvider CreateFakeProvider(IServiceProvider sp, string name)
{
    var runtime = sp.GetRequiredService<FakeProviderRuntime>();
    var config = sp.GetRequiredService<IOptions<ProviderOptions>>().Value.Providers[name];
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<FakeProvider>();
    return new FakeProvider(name, runtime, config, logger);
}