using Chess.Web.Components;
using Chess.Application;
using Chess.Infrastructure;
using Chess.Web.Hubs;
using Chess.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(CreateOnlineGame).Assembly));
builder.Services.AddSingleton<IOnlineMatchStore, InMemoryOnlineMatchStore>();
builder.Services.AddScoped<OnlineGameClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<ChessHub>("/gamehub");

app.Run();
