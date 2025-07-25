// BreakoutOnline/Program.cs

using BreakoutOnline.DataAccess; // Adicionar este using
using BreakoutOnline.Hubs;
using Microsoft.EntityFrameworkCore; // Adicionar este using

var builder = WebApplication.CreateBuilder(args);

// [MUDANÇA] Configuração do Banco de Dados SQLite
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.AllowAnyHeader()
                                .AllowAnyMethod()
                                .SetIsOriginAllowed((host) => true)
                                .AllowCredentials();
                      });
});

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var app = builder.Build();

// [MUDANÇA] Garante que o banco de dados seja criado na inicialização
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<GameDbContext>();
    context.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseAuthorization();
app.MapRazorPages();
app.MapFallbackToFile("/index.html");
app.MapHub<GameHub>("/gameHub");

app.Run();