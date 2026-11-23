using System.Text.Json.Serialization;
using ErpVendas.Api.Domain;
using ErpVendas.Api.Infra;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// SQL Server em produção; SQLite nos testes de integração.
builder.Services.AddDbContext<ErpDbContext>(options =>
{
    if (builder.Configuration["Database:Provider"] == "Sqlite")
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
    else
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
});

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Regra de negócio → 409; conflito de concorrência no estoque → 409; o resto → 500 sem detalhes internos.
app.UseExceptionHandler(erro => erro.Run(async context =>
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, mensagem) = ex switch
    {
        RegraDeNegocioException r => (StatusCodes.Status409Conflict, r.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "O estoque foi alterado por outra operação. Tente novamente."),
        _ => (StatusCodes.Status500InternalServerError, "Erro inesperado.")
    };
    await Results.Problem(mensagem, statusCode: status).ExecuteAsync(context);
}));

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("ERP Vendas - API"));
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
    if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();

    if (app.Configuration.GetValue("Seed:DadosIniciais", false)) await DadosIniciais.CriarSeVazioAsync(db);
}

app.Run();

public partial class Program;
