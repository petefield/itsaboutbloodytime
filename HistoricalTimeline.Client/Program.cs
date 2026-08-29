using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HistoricalTimeline.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "/";
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl)
});

await builder.Build().RunAsync();
