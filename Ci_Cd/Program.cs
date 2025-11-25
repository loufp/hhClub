using Ci_Cd.Services;

var builder = WebApplication.CreateBuilder(args);


// Разрешаем CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//сервисы
builder.Services.AddScoped<IGitServices, GitServices>();
builder.Services.AddScoped<IAnalyzerService, AnalyzerService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IExecutionService, ExecutionService>();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

var app = builder.Build();

// пеплайн

app.UseDefaultFiles(); 
app.UseStaticFiles();  // Разрешает отдавать файлы

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Включаем CORS
app.UseCors("AllowAll");

app.MapControllers();

app.Run();