using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MedTracker;
using MedTracker.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<Storage>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<SchedulerService>();

await builder.Build().RunAsync();
