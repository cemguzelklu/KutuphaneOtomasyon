using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Services.AI;
using KutuphaneOtomasyon.Services.Chat;
using KutuphaneOtomasyon.Services.Hosted;
using KutuphaneOtomasyon.Services.Recommendations;
using KutuphaneOtomasyon.Services.Risk;
using Microsoft.EntityFrameworkCore;
// Gerekirse:
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Authentication;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
// User-secrets'i yükle
builder.Configuration.AddUserSecrets<Program>(optional: true);

// (Opsiyonel) hýzlý doðrulama
var oa = builder.Configuration.GetSection("OpenAI");
Console.WriteLine($"[CFG] OpenAI BaseUrl={oa["BaseUrl"]}, Model={oa["Model"]}, ApiKey={(string.IsNullOrWhiteSpace(oa["ApiKey"]) ? "(empty)" : "(set)")}");

// Db
builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// **NAMED CLIENT**: "fast-http"
builder.Services.AddHttpClient("fast-http", client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;           // timeout'u CTS kontrol etsin
    client.DefaultRequestHeaders.ExpectContinue = false;
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectTimeout = TimeSpan.FromSeconds(10), // baðlantý kurulana kadar
    UseProxy = true,                     // sistem proxy'sini kullan
    Proxy = WebRequest.DefaultWebProxy,
    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
    {
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        // !!! Sadece kurumsal sertifika sorunu varsa test amaçlý aç:
        // RemoteCertificateValidationCallback = (_, __, ___, ____) => true
    }
});
builder.Services.AddHostedService<WarmupAiHostedService>();
// MVC & servisler
builder.Services.AddControllersWithViews();
builder.Services.AddSession();
builder.Services.AddScoped<IAiLogService, AiLogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRiskScoringService, RiskScoringService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IAiAssistant, OpenAiAssistant>();
builder.Services.AddScoped<OpenAiChatService>();
builder.Services.AddScoped<LocalChatService>();
builder.Services.AddScoped<IChatService>(sp =>
{
    var primary = sp.GetRequiredService<OpenAiChatService>();
    var local = sp.GetRequiredService<LocalChatService>();
    return new SmartChatService(primary, local);
});

var app = builder.Build();

// pipeline...
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
